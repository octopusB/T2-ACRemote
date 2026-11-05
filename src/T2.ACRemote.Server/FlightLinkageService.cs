using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using T2.ACRemote.Common;

namespace T2.ACRemote.Server
{
    public sealed class FlightLinkageService
    {
        private readonly BridgeRegistry _registry;
        private readonly IFlightDataProvider _provider;
        private readonly ConcurrentDictionary<string, bool> _enabled = new ConcurrentDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, BridgeFlightLinkageState> _states = new ConcurrentDictionary<string, BridgeFlightLinkageState>(StringComparer.OrdinalIgnoreCase);
        private readonly object _settingsLock = new object();
        private volatile bool _stopping;
        private Thread _thread;
        private int _startBeforeMinutes;
        private int _stopAfterMinutes;
        private IList<FlightRecord> _flights = new List<FlightRecord>();
        private DateTime _flightsUpdatedUtc;

        public FlightLinkageService(BridgeRegistry registry, IFlightDataProvider provider, int startBeforeMinutes, int stopAfterMinutes)
        {
            _registry = registry;
            _provider = provider;
            UpdateSettings(startBeforeMinutes, stopAfterMinutes);
        }

        public void Start()
        {
            _thread = new Thread(Run) { IsBackground = true, Name = "FlightLinkage" };
            _thread.Start();
        }

        public void Stop()
        {
            _stopping = true;
            if (_thread != null) _thread.Join(3000);
        }

        public void UpdateSettings(int startBeforeMinutes, int stopAfterMinutes)
        {
            if (startBeforeMinutes < 0 || startBeforeMinutes > 240) throw new ArgumentOutOfRangeException("startBeforeMinutes", "提前开启分钟数必须在 0-240 之间。");
            if (stopAfterMinutes < 0 || stopAfterMinutes > 240) throw new ArgumentOutOfRangeException("stopAfterMinutes", "航班后关闭分钟数必须在 0-240 之间。");
            lock (_settingsLock)
            {
                _startBeforeMinutes = startBeforeMinutes;
                _stopAfterMinutes = stopAfterMinutes;
            }
        }

        public void SetBridgeEnabled(string bridgeId, bool enabled)
        {
            if (string.IsNullOrWhiteSpace(bridgeId)) throw new ArgumentException("Bridge ID 不能为空。");
            _enabled[bridgeId] = enabled;
            BridgeFlightLinkageState state;
            if (_states.TryGetValue(bridgeId, out state)) state.Enabled = enabled;
            if (!enabled) ThreadPool.QueueUserWorkItem(_ => ReleaseBridge(bridgeId));
        }

        private void ReleaseBridge(string bridgeId)
        {
            var session = _registry.Get(bridgeId);
            if (session != null)
            {
                try
                {
                    var result = session.Send(ControlMode.Release, 10, 5000);
                    var state = _states.GetOrAdd(bridgeId, CreateState);
                    state.LastAction = "关闭航班联动并释放控制";
                    state.LastActionUtc = DateTime.UtcNow;
                    state.LastError = result.Success ? null : result.Message;
                }
                catch (Exception ex)
                {
                    var state = _states.GetOrAdd(bridgeId, CreateState);
                    state.LastError = ex.Message;
                }
            }
        }

        public void SetAllBridgesEnabled(bool enabled)
        {
            foreach (var bridgeId in _registry.BridgeIds()) SetBridgeEnabled(bridgeId, enabled);
        }

        public FlightLinkageSnapshot Snapshot()
        {
            var bridgeIds = _registry.BridgeIds().Union(_enabled.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
            var states = new List<BridgeFlightLinkageState>();
            foreach (var bridgeId in bridgeIds)
            {
                BridgeFlightLinkageState state;
                if (!_states.TryGetValue(bridgeId, out state)) state = CreateState(bridgeId);
                states.Add(CloneState(state));
            }
            return new FlightLinkageSnapshot
            {
                Settings = Settings(),
                Bridges = states,
                FlightCount = _flights.Count,
                FlightsUpdatedUtc = _flightsUpdatedUtc,
                DataSource = _provider.Name
            };
        }

        public FlightLinkageSettings Settings()
        {
            int before, after;
            lock (_settingsLock) { before = _startBeforeMinutes; after = _stopAfterMinutes; }
            var bridgeIds = _registry.BridgeIds();
            return new FlightLinkageSettings
            {
                StartBeforeMinutes = before,
                StopAfterMinutes = after,
                AllEnabled = bridgeIds.Count > 0 && bridgeIds.All(IsEnabled),
                UpdatedUtc = DateTime.UtcNow
            };
        }

        public IList<FlightRecord> Flights(string standId)
        {
            var query = _flights.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(standId)) query = query.Where(x => string.Equals(x.StandId, NormalizeStandId(standId), StringComparison.OrdinalIgnoreCase));
            return query.OrderBy(x => x.EstimatedArrivalUtc).ToList();
        }

        public bool IsEnabled(string bridgeId)
        {
            bool value;
            return _enabled.TryGetValue(bridgeId, out value) && value;
        }

        public static string NormalizeStandId(string bridgeId)
        {
            return FlightLinkageRules.NormalizeStandId(bridgeId);
        }

        public static bool ShouldRun(DateTime utcNow, IEnumerable<FlightRecord> flights, int startBeforeMinutes, int stopAfterMinutes)
        {
            return FlightLinkageRules.ShouldRun(utcNow, flights.Select(x => x.EstimatedArrivalUtc), startBeforeMinutes, stopAfterMinutes);
        }

        private void Run()
        {
            while (!_stopping)
            {
                try { Tick(DateTime.UtcNow); }
                catch (Exception ex) { Console.WriteLine("{0:O} 航班联动调度异常: {1}", DateTime.UtcNow, ex.Message); }
                for (var i = 0; i < 10 && !_stopping; i++) Thread.Sleep(500);
            }
        }

        private void Tick(DateTime now)
        {
            _flights = _provider.GetFlights(now);
            _flightsUpdatedUtc = now;
            int before, after;
            lock (_settingsLock) { before = _startBeforeMinutes; after = _stopAfterMinutes; }

            foreach (var bridgeId in _registry.BridgeIds())
            {
                var standId = NormalizeStandId(bridgeId);
                var relevant = _flights.Where(x => string.Equals(x.StandId, standId, StringComparison.OrdinalIgnoreCase)).OrderBy(x => x.EstimatedArrivalUtc).ToList();
                var shouldRun = ShouldRun(now, relevant, before, after);
                var state = _states.GetOrAdd(bridgeId, CreateState);
                state.BridgeId = bridgeId;
                state.StandId = standId;
                state.Enabled = IsEnabled(bridgeId);
                state.ShouldRun = shouldRun;
                state.RelevantFlights = relevant;
                state.AutomationStatus = !state.Enabled ? "未启用" : shouldRun ? "航班窗口内，应开启" : "无有效航班，应关闭";
                if (state.Enabled) ApplyDesiredState(state, shouldRun, now);
            }
        }

        private void ApplyDesiredState(BridgeFlightLinkageState automation, bool shouldRun, DateTime now)
        {
            var session = _registry.Get(automation.BridgeId);
            if (session == null) return;
            var status = session.LastStatus;
            if (status == null) return;

            var satisfied = shouldRun
                ? status.BridgeRunning || (status.Do1RemoteStart && !status.Do2CutOriginal)
                : status.BridgeRunning ? status.Do2CutOriginal && !status.Do1RemoteStart : !status.Do1RemoteStart && !status.Do2CutOriginal;
            if (satisfied) { automation.LastError = null; return; }
            if (automation.LastActionUtc.HasValue && (now - automation.LastActionUtc.Value).TotalSeconds < 15) return;

            try
            {
                var mode = shouldRun ? ControlMode.RemoteStart : ControlMode.RemoteStop;
                var result = session.Send(mode, 3600, 5000);
                automation.LastAction = shouldRun ? "自动开启" : "自动关闭";
                automation.LastActionUtc = now;
                automation.LastError = result.Success ? null : result.Message;
                Console.WriteLine("{0:O} 航班联动 {1} {2}: {3}", now, automation.BridgeId, automation.LastAction, result.Message);
            }
            catch (Exception ex)
            {
                automation.LastActionUtc = now;
                automation.LastError = ex.Message;
            }
        }

        private BridgeFlightLinkageState CreateState(string bridgeId)
        {
            return new BridgeFlightLinkageState
            {
                BridgeId = bridgeId,
                StandId = NormalizeStandId(bridgeId),
                Enabled = IsEnabled(bridgeId),
                AutomationStatus = "等待调度",
                RelevantFlights = new List<FlightRecord>()
            };
        }

        private static BridgeFlightLinkageState CloneState(BridgeFlightLinkageState x)
        {
            return new BridgeFlightLinkageState
            {
                BridgeId = x.BridgeId,
                StandId = x.StandId,
                Enabled = x.Enabled,
                ShouldRun = x.ShouldRun,
                AutomationStatus = x.AutomationStatus,
                LastAction = x.LastAction,
                LastActionUtc = x.LastActionUtc,
                LastError = x.LastError,
                RelevantFlights = x.RelevantFlights == null ? new List<FlightRecord>() : x.RelevantFlights.ToList()
            };
        }
    }
}
