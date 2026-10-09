using System;
using LineTrace;
using UnityEngine;

namespace Display
{
    public class CrossDisp : MonoBehaviour
    {
        public Color color;

        private int num;

        private Material myMat;

        private Nats nats;

        private float queryInterval = 0.5f; // Query every 0.5s instead of every frame
        private float timer;
        private bool lastFailed = false;

        private void Awake()
        {
            myMat = GetComponent<MeshRenderer>().material;
            try { nats = new Nats(); } catch { nats = null; }

            num = int.Parse(name);
        }

        private void Update()
        {
            if (nats == null || !nats.IsConnected) return;
            if (lastFailed) return; // Stop querying after first failure (no responder = no arbitrator)

            timer += Time.deltaTime;
            if (timer < queryInterval) return;
            timer = 0f;

            if (!DispQuery.TryQuery(nats, num, -1, out bool occupied))
            {
                lastFailed = true;
                return;
            }
            myMat.color = occupied ? color : Color.white;
        }

        private void OnDestroy()
        {
            Destroy(myMat);
        }
    }

    [Serializable]
    public struct Question
    {
        public int Cross1;
        public int Cross2;
    }
}
