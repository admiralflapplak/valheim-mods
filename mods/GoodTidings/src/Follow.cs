using UnityEngine;

namespace GoodTidings
{
    /// <summary>
    /// Keeps an object on a point (the rider on the Lox's seat), the way the game moves a riding player each frame. Parenting to the seat
    /// would take on its scale too: the Lox's "attachpoint" is stretched (about 1 x 2 x 2), and the rider came out bigger.
    /// </summary>
    internal sealed class Follow : MonoBehaviour
    {
        public Transform Target;
        /// <summary>Metres up and back from the point, measured along <see cref="Frame"/> (the Lox itself: the seat's own axes are tilted).</summary>
        public float Up, Back, Right;
        public Transform Frame;
        /// <summary>Turned with <see cref="Frame"/> (the Lox) rather than the point (saddle bags hang level, not tilted like the seat).</summary>
        public bool FrameRotation;

        private void LateUpdate()
        {
            if (Target == null) { Destroy(gameObject); return; }
            Vector3 up = Frame ? Frame.up : Vector3.up, forward = Frame ? Frame.forward : Target.forward, right = Frame ? Frame.right : Target.right;
            transform.SetPositionAndRotation(Target.position + up * Up - forward * Back + right * Right,
                FrameRotation && Frame ? Frame.rotation : Target.rotation);
        }
    }
}
