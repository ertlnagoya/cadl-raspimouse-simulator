using Unity.Cinemachine;
using RasPiMouse.Device;
using UnityEngine;
using UnityEngine.UI;

namespace Display
{
    public class CamOp : MonoBehaviour
    {
        private const float RotationSpeed = 40f;

        public string search;

        private string preSearch;

        public Transform pivot;
        private Transform pre;
        public RawImage image;
        private Transform empty;


        private void Start()
        {
            empty = new GameObject().transform;
            // The scenes still carry the Cinemachine 2.x CinemachineVirtualCamera,
            // which Cinemachine 3.x keeps as a deprecated component. Look the
            // camera up by the common base class so that both it and the 3.x
            // CinemachineCamera are found.
            var cam = GetComponent<CinemachineVirtualCameraBase>();
            if (cam != null)
            {
                cam.Follow = empty;
            }
            else
            {
                Debug.LogWarning("[CamOp] No Cinemachine camera on this object. Camera follow disabled.");
            }
        }

        void Update()
        {
            if (search != preSearch)
            {
                preSearch = search;
                var hit = GameObject.Find(search);
                if (hit)
                {
                    pivot = hit.transform;
                }
            }

            if (pre != pivot && pivot)
            {
//                var tmp = pivot.Find("base_footprint");
//                if (tmp)
//                {
//                    tmp = tmp.Find("base_link");
//                    if (tmp)
//                    {
//                        pivot = tmp;
//                    }
//                }

                var mini = pivot.GetComponentInChildren<MiniCamera>();
                if (mini && image) image.texture = mini.GetTexture();

                pre = pivot;
            }

            var tf = transform;
            var eu = tf.eulerAngles;
            if (Input.GetKey(KeyCode.A))
            {
                eu.y += RotationSpeed * Time.deltaTime;
            }
            else if (Input.GetKey(KeyCode.D))
            {
                eu.y -= RotationSpeed * Time.deltaTime;
            }

            if (Input.GetKey(KeyCode.W))
            {
                eu.x += RotationSpeed * Time.deltaTime;
            }
            else if (Input.GetKey(KeyCode.S))
            {
                eu.x -= RotationSpeed * Time.deltaTime;
            }


            tf.rotation = Quaternion.Euler(eu);
            if (empty)
            {
                eu.x = 0;
                eu.z = 0;
                empty.rotation = Quaternion.Euler(eu);
            }
        }

        private void LateUpdate()
        {
            if (empty && pivot)
            {
                empty.position = pivot.position;
            }
        }
    }
}