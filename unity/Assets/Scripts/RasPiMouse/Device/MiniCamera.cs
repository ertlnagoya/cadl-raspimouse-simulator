using UnityEngine;

namespace RasPiMouse.Device
{
    public class MiniCamera : MonoBehaviour
    {
        private RenderTexture _rt;
        private Camera _camera;

        private void Awake()
        {
            _rt = new RenderTexture(256, 256, 0);
            _camera = GetComponent<Camera>();
            _camera.targetTexture = _rt;
        }

        public RenderTexture GetTexture() => _rt;

        private void OnDestroy()
        {
            Destroy(_rt);
        }
    }
}