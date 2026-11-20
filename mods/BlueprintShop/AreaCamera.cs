using System.Linq;
using Cinemachine;
using UnityEngine;

namespace BlueprintShop
{
    /// <summary>
    /// The camera while "copy from the floor" is drawing a box (user feedback 2026-11-20 on
    /// 0.7.0: look straight down, and let the player pan, as Easy Floor Planner's middle-drag
    /// does outside the shop). Local to this machine; nothing is networked.
    ///
    /// Same technique as Easy Floor Planner's editor camera (FloorPlanCamera, which works in
    /// game): the game's camera is one Cinemachine virtual camera whose FramingTransposer frames
    /// a CinemachineTargetGroup (vanilla's ViewBoundsMaintainer fills it with the restaurant's
    /// bound markers). While active, the group's targets are swapped for two anchors at the
    /// corners of the current framing:
    ///   - pan: move both anchors;
    ///   - zoom: move them apart or together (the game's own framing zooms to fit them);
    ///   - top-down: the virtual camera has no Aim stage, so it keeps its transform's rotation;
    ///     its pitch is eased to 89 degrees (yaw kept), and back afterwards.
    /// If vanilla replaces the group's targets meanwhile, those become the ones to restore.
    /// A small driver object keeps this going every frame, so the tilt back can finish after
    /// the shop window that used it has gone (the day can start while it's open).
    /// </summary>
    internal static class AreaCamera
    {
        private const float TopDownPitch = 89f; // not exactly 90: keeps "up the screen" well defined
        private const float TiltDegreesPerSecond = 220f;
        private const float ZoomStep = 1.15f;
        private const float MinHalfSize = 2f;   // tiles from the centre to the edge of the framed box
        private const float MaxZoomOut = 2.5f;  // times the starting box
        private const float PanMargin = 8f;     // tiles past the starting view the centre may go

        private static CinemachineTargetGroup Group;
        private static CinemachineTargetGroup.Target[] Saved;
        private static CinemachineTargetGroup.Target[] Ours;
        private static GameObject AnchorRoot;
        private static Transform Min, Max;
        private static Vector3 StartCentre;
        private static Vector2 StartHalf;
        private static Vector2 Half;

        private static Transform Vcam;
        private static Quaternion VcamOriginal;
        private static GameObject DriverObject;

        /// <summary>Local co-op players share one camera: it stays top-down while any of them is drawing.</summary>
        private static readonly System.Collections.Generic.HashSet<object> Users = new System.Collections.Generic.HashSet<object>();

        public static bool Active => Group != null && AnchorRoot != null;

        public static void Begin(object user)
        {
            Users.Add(user);
            if (Active)
            {
                return;
            }
            Group = Object.FindObjectOfType<CinemachineTargetGroup>();
            if (Group == null)
            {
                ShopLog.Warn("No camera target group found; copy from the floor keeps the normal view.");
                return;
            }
            Saved = Group.m_Targets;

            // The current framing: every live target, with its radius.
            bool any = false;
            Bounds box = default;
            foreach (CinemachineTargetGroup.Target t in Saved ?? new CinemachineTargetGroup.Target[0])
            {
                if (t.target == null || t.weight <= 0f)
                {
                    continue;
                }
                var b = new Bounds(t.target.position, Vector3.one * (t.radius * 2f));
                if (any)
                {
                    box.Encapsulate(b);
                }
                else
                {
                    box = b;
                    any = true;
                }
            }
            if (!any)
            {
                box = new Bounds(Vector3.zero, new Vector3(16f, 0f, 12f));
            }

            StartCentre = box.center;
            StartHalf = new Vector2(Mathf.Max(box.extents.x, MinHalfSize), Mathf.Max(box.extents.z, MinHalfSize));
            Half = StartHalf;
            AnchorRoot = new GameObject("BlueprintShop Camera Anchors");
            AnchorRoot.transform.position = StartCentre;
            Min = new GameObject("Min").transform;
            Max = new GameObject("Max").transform;
            Min.SetParent(AnchorRoot.transform, false);
            Max.SetParent(AnchorRoot.transform, false);
            ApplyHalf();
            Ours = new[]
            {
                new CinemachineTargetGroup.Target { target = Min, weight = 1f, radius = 0f },
                new CinemachineTargetGroup.Target { target = Max, weight = 1f, radius = 0f },
            };
            Group.m_Targets = Ours;

            // The virtual camera following this group (the one the game created).
            if (Vcam == null)
            {
                CinemachineVirtualCamera vcam = Object.FindObjectsOfType<CinemachineVirtualCamera>()
                    .FirstOrDefault(c => c.Follow == Group.transform)
                    ?? Object.FindObjectOfType<CinemachineVirtualCamera>();
                if (vcam != null)
                {
                    Vcam = vcam.transform;
                    VcamOriginal = Vcam.rotation;
                }
            }
            // (If Vcam is still set, a previous tilt back hasn't finished: keep its original rotation.)
            if (DriverObject == null)
            {
                DriverObject = new GameObject("BlueprintShop Camera Driver");
                DriverObject.AddComponent<Driver>();
            }
            ShopLog.Debug(() => $"Copy from the floor: top-down view around {StartCentre}, {box.size.x:0.#} x {box.size.z:0.#}");
        }

        public static void End(object user)
        {
            Users.Remove(user);
            if (Users.Count == 0)
            {
                Release();
            }
        }

        private static void Release()
        {
            if (Group != null && Group.m_Targets == Ours)
            {
                Group.m_Targets = Saved ?? new CinemachineTargetGroup.Target[0];
            }
            if (AnchorRoot != null)
            {
                Object.Destroy(AnchorRoot);
            }
            AnchorRoot = null;
            Ours = null;
            Saved = null;
            Group = null;
            // The driver keeps running until the camera has tilted back.
        }

        /// <summary>Moves the view by a world offset along the ground.</summary>
        public static void Move(Vector3 delta)
        {
            if (!Active)
            {
                return;
            }
            Vector3 p = AnchorRoot.transform.position + new Vector3(delta.x, 0f, delta.z);
            float limitX = StartHalf.x * MaxZoomOut + PanMargin, limitZ = StartHalf.y * MaxZoomOut + PanMargin;
            p.x = Mathf.Clamp(p.x, StartCentre.x - limitX, StartCentre.x + limitX);
            p.z = Mathf.Clamp(p.z, StartCentre.z - limitZ, StartCentre.z + limitZ);
            AnchorRoot.transform.position = p;
        }

        /// <summary>Pans by a screen-relative direction (-1..1 per axis), in views per second.</summary>
        public static void PanScreen(Vector2 direction, float viewsPerSecond, float dt)
        {
            if (!Active || direction.sqrMagnitude < 0.0001f)
            {
                return;
            }
            Vector2 v = Vector2.ClampMagnitude(direction, 1f);
            Move((Right() * (v.x * Half.x) + Up() * (v.y * Half.y)) * (2f * viewsPerSecond * dt));
        }

        /// <summary>Pans by a mouse movement in pixels, so the floor follows the pointer ("grab").</summary>
        public static void PanPixels(Vector2 pixels)
        {
            Camera cam = Camera.main;
            if (!Active || cam == null || pixels == Vector2.zero || Screen.width <= 0)
            {
                return;
            }
            // World units per pixel across the middle of the screen.
            if (!Ground(cam, new Vector2(0f, Screen.height * 0.5f), out Vector3 left)
                || !Ground(cam, new Vector2(Screen.width, Screen.height * 0.5f), out Vector3 right))
            {
                return;
            }
            float perPixel = Vector3.Distance(left, right) / Screen.width;
            Move(-(Right() * pixels.x + Up() * pixels.y) * perPixel);
        }

        /// <summary>Zooms in (steps &gt; 0) or out (steps &lt; 0) by mouse-wheel steps.</summary>
        public static void Zoom(float steps)
        {
            if (!Active || steps == 0f)
            {
                return;
            }
            float factor = Mathf.Pow(ZoomStep, -steps);
            Half = new Vector2(
                Mathf.Clamp(Half.x * factor, MinHalfSize, StartHalf.x * MaxZoomOut),
                Mathf.Clamp(Half.y * factor, MinHalfSize, StartHalf.y * MaxZoomOut));
            ApplyHalf();
        }

        private static void ApplyHalf()
        {
            Min.localPosition = new Vector3(-Half.x, 0f, -Half.y);
            Max.localPosition = new Vector3(Half.x, 0f, Half.y);
        }

        private static bool Ground(Camera cam, Vector2 screen, out Vector3 point)
        {
            Ray ray = cam.ScreenPointToRay(screen);
            if (new Plane(Vector3.up, Vector3.zero).Raycast(ray, out float d))
            {
                point = ray.GetPoint(d);
                return true;
            }
            point = default;
            return false;
        }

        /// <summary>The camera's right and up axes, flattened onto the floor ("right/up the screen").</summary>
        private static Vector3 Right() => Flat(Camera.main != null ? Camera.main.transform.right : Vector3.right, Vector3.right);

        private static Vector3 Up() => Flat(Camera.main != null ? Camera.main.transform.up : Vector3.forward, Vector3.forward);

        private static Vector3 Flat(Vector3 v, Vector3 fallback)
        {
            v.y = 0f;
            return v.sqrMagnitude > 0.0001f ? v.normalized : fallback;
        }

        /// <summary>Keeps our targets in the group (vanilla replaces the array when a marker view changes).</summary>
        private static void Maintain()
        {
            if (!Active)
            {
                return;
            }
            if (Group.m_Targets != Ours)
            {
                Saved = Group.m_Targets;
                Group.m_Targets = Ours;
            }
        }

        /// <summary>Eases the tilt towards top-down while active, and back afterwards; then stops.</summary>
        private static void Tick()
        {
            Maintain();
            if (Vcam == null)
            {
                Object.Destroy(DriverObject);
                DriverObject = null;
                return;
            }
            Quaternion goal = Active
                ? Quaternion.Euler(TopDownPitch, VcamOriginal.eulerAngles.y, 0f)
                : VcamOriginal;
            Vcam.rotation = Quaternion.RotateTowards(Vcam.rotation, goal, TiltDegreesPerSecond * Time.unscaledDeltaTime);
            if (!Active && Quaternion.Angle(Vcam.rotation, VcamOriginal) < 0.01f)
            {
                Vcam.rotation = VcamOriginal;
                Vcam = null;
                Object.Destroy(DriverObject);
                DriverObject = null;
            }
        }

        private class Driver : MonoBehaviour
        {
            private void Update() => Maintain();

            private void LateUpdate() => Tick();

            private void OnDestroy()
            {
                // Scene change (leaving the restaurant): the camera objects are going too.
                if (DriverObject == gameObject)
                {
                    Users.Clear();
                    Release();
                    Vcam = null;
                    DriverObject = null;
                }
            }
        }
    }
}
