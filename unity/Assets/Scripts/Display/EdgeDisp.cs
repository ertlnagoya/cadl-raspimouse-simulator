using LineTrace;
using UnityEngine;

namespace Display
{
    public class EdgeDisp : MonoBehaviour
    {
        public Color color;

        public int num0;
        public int num1;

        private Material myMat;

        private Nats nats;

        private float queryInterval = 0.5f;
        private float timer;
        private bool lastFailed = false;

        private void Awake()
        {
            myMat = GetComponent<MeshRenderer>().material;
            try { nats = new Nats(); } catch { nats = null; }

            var tmp = name.Split("-");
            if (tmp.Length >= 2)
            {
                num0 = int.Parse(tmp[0]);
                num1 = int.Parse(tmp[1]);
            }
        }

        private void Update()
        {
            if (nats == null || !nats.IsConnected) return;
            if (lastFailed) return;

            timer += Time.deltaTime;
            if (timer < queryInterval) return;
            timer = 0f;

            if (!DispQuery.TryQuery(nats, num0, num1, out bool occupied))
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
}
