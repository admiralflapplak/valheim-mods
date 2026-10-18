using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace WeaponOils
{
    /// <summary>
    /// Lowers the press's ram into the barrel as the oil matures: down by the part of the time that has passed, turning as a screw does, and
    /// back up when the oil is ready or the barrel is empty. Reads the fermenter's own saved start time (what its hover text uses), so every
    /// player sees the same, and a press loaded half way through is found half way down. It sits on the press prefab, next to the Fermenter.
    /// </summary>
    internal sealed class PressAnimator : MonoBehaviour
    {
        private static readonly MethodInfo FermentationTime = AccessTools.Method(typeof(Fermenter), "GetFermentationTime");
        private const float TurnsAtTheBottom = 1.5f;
        private const float SecondsToRise = 4f;

        private Fermenter _fermenter;
        private ZNetView _nview;
        private Transform _ram;
        private float _depth = -1f;     // 0 = raised, 1 = pressed right down; -1 until the first look
        private float _target;
        private float _nextLook;

        private void Awake()
        {
            _fermenter = GetComponent<Fermenter>();
            _nview = GetComponent<ZNetView>();
            _ram = transform.Find("nick_visual/nick_frame/ram");
        }

        private void Update()
        {
            if (_ram == null || _fermenter == null || _nview == null || _nview.GetZDO() == null) return;
            if (Time.time >= _nextLook)
            {
                _nextLook = Time.time + 0.5f;
                _target = Look();
            }
            float step = Time.deltaTime / SecondsToRise;
            // (the ram comes up quickly when released, and follows the slow pressing down at the pace of the clock)
            _depth = _depth < 0f ? _target : (_target > _depth ? Mathf.MoveTowards(_depth, _target, step * 0.1f) : Mathf.MoveTowards(_depth, _target, step));
            _ram.localPosition = new Vector3(0f, -OilPress.RamTravel * _depth, 0f);
            _ram.localRotation = Quaternion.Euler(0f, 360f * TurnsAtTheBottom * _depth, 0f);
        }

        /// <summary>How far down the ram should be: with the part of the maturing time that has passed; raised otherwise.</summary>
        private float Look()
        {
            if (_nview.GetZDO().GetInt(ZDOVars.s_content) == 0 || ZNet.instance == null) return 0f;
            double passed = (double)FermentationTime.Invoke(_fermenter, null);
            float duration = _fermenter.m_fermentationDuration;
            if (passed < 0 || duration <= 0f || passed >= duration) return 0f;
            return Mathf.Clamp01((float)(passed / duration));
        }
    }
}
