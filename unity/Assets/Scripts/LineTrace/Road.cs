using System;
using UnityEngine;

namespace LineTrace
{
    public class Road : MonoBehaviour
    {
        private Nats nats;

        private void Awake()
        {
            try { nats = new Nats(); } catch { nats = null; }
        }

        private void OnDestroy()
        {
            nats?.Send("fin", new Demand());
        }
    }
}