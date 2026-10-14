using UnityEngine;

namespace FulingHeckler
{
    /// <summary>
    /// Sits on a rigid helmet copy and applies a Fit: offsets in the Fuling's own directions (up, forward,
    /// side), tilt and turn, and size. It remembers how his head bone sat in his rest pose, so "up" always
    /// means up from his head even though the bone itself is oriented in some arbitrary way.
    /// </summary>
    internal class HelmetFitter : MonoBehaviour
    {
        public string ItemName;

        private Quaternion _restRel = Quaternion.identity; // head bone's rotation relative to his body, at rest
        private float _boneScale = 1f;
        private Vector3 _itemScale = Vector3.one;           // the helmet's real-world size

        public void Init(Transform body, Transform joint, Vector3 itemWorldScale)
        {
            _restRel = Quaternion.Inverse(body.rotation) * joint.rotation;
            float s = joint.lossyScale.x;
            _boneScale = Mathf.Abs(s) > 0.0001f ? s : 1f;
            _itemScale = itemWorldScale;
        }

        public void Apply(Fit f)
        {
            if (f == null) f = Fit.Identity;
            var inv = Quaternion.Inverse(_restRel);

            // Position: body-space offset (metres) turned into the bone's own space.
            Vector3 offset = new Vector3(f.Side, f.Up, f.Forward);
            transform.localPosition = (inv * offset) / _boneScale;

            // Rotation: tilt (nod forward/back) and turn (left/right), around his body's axes.
            Quaternion bodyRot = Quaternion.AngleAxis(f.Turn, Vector3.up) * Quaternion.AngleAxis(f.Tilt, Vector3.right);
            transform.localRotation = inv * bodyRot * _restRel;

            // Size: real-world size times the fit's size, undoing the bone's own odd scale.
            Vector3 bone = transform.parent != null ? transform.parent.lossyScale : Vector3.one;
            Vector3 want = _itemScale * (f.Size <= 0f ? 1f : f.Size);
            transform.localScale = new Vector3(Div(want.x, bone.x), Div(want.y, bone.y), Div(want.z, bone.z));
        }

        private static float Div(float a, float b) => Mathf.Abs(b) < 0.0001f ? a : a / b;
    }
}
