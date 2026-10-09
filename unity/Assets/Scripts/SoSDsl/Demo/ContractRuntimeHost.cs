// <auto-edited-on-import="false">
//   Hand-written. Lives next to the generated Runtime/ but is owned
//   by the host project, not the codegen.
// </auto-edited-on-import>
using System.Collections.Generic;
using UnityEngine;

namespace CADL.SosDsl.Demo {

    /// <summary>
    /// Scene-wide singleton MonoBehaviour that owns the SoS-DSL
    /// ContractRuntime. The Pilot↔contract bridges and the demo
    /// harness both talk to this host so all contract instances
    /// share one runtime, one event bus, and one world model.
    ///
    /// Drop a single empty GameObject named "ContractRuntimeHost"
    /// into the scene root and attach this component. It is marked
    /// DontDestroyOnLoad so it survives scene transitions.
    /// </summary>
    public sealed class ContractRuntimeHost : MonoBehaviour {

        public static ContractRuntimeHost Instance { get; private set; }

        [Header("Logging")]
        [Tooltip("If true, every lifecycle / violation event is also "
                 + "echoed to UnityEngine.Debug.Log.")]
        public bool logToConsole = true;

        public ContractRuntime Runtime { get; private set; }

        private readonly List<ContractEvent> _trace =
            new List<ContractEvent>();

        public IReadOnlyList<ContractEvent> Trace => _trace;

        private void Awake() {
            if (Instance != null && Instance != this) {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            Runtime = new ContractRuntime();
            Runtime.Events.Subscribe(OnContractEvent);
        }

        /// <summary>Sim-time clock in milliseconds (Time.time-based).</summary>
        public long NowMs => (long)(Time.time * 1000f);

        private void Update() {
            Runtime.Tick(NowMs);
        }

        public void Post(string eventName,
                         IDictionary<string, object> payload = null) {
            Runtime.PostEvent(eventName, NowMs, payload);
        }

        public void UpdateWorld(IDictionary<string, object> snapshot) {
            Runtime.UpdateWorld(snapshot);
        }

        private void OnContractEvent(ContractEvent ev) {
            _trace.Add(ev);
            if (logToConsole) Debug.Log(ev.ToString());
        }
    }
}
