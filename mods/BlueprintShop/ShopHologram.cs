using UnityEngine;

namespace BlueprintShop
{
    /// <summary>
    /// The shop terminal's hologram: a shopping cart instead of the reroll terminal's dice (user
    /// request 2026-11-19: "Shopping cart", in the game's hologram blue).
    ///
    /// Same technique as Easy Floor Planner's Floor Plan terminal (FloorPlanHologram, surveyed
    /// in-game there): the reroll-trigger model's root Animator spins a "Container" holding two
    /// "Dice", drawn with the glow materials "Glowing Blue Soft" and "Glowing Blue Dark Soft".
    /// The dice are hidden and the cart is built from simple meshes inside the same Container,
    /// so it spins and floats with the game's own animation and glows like the game's other
    /// holograms. Visual only, on every machine, applied from TerminalLabelPatch to shop
    /// terminals only (Easy Floor Planner's terminal and ordinary reroll triggers are untouched).
    /// </summary>
    internal static class ShopHologram
    {
        private const string Name = "BlueprintShop Cart Hologram";
        private static Mesh Cube, Cylinder;

        /// <summary>Hides the dice and adds the cart (once per view).</summary>
        public static void Apply(Component view)
        {
            Transform container = view.transform.Find("Container");
            if (container == null || container.Find(Name) != null)
            {
                return; // not the expected model, or already done
            }
            Renderer dice = null;
            foreach (Transform child in container)
            {
                if (child.name.StartsWith("Dice"))
                {
                    Renderer r = child.GetComponent<Renderer>();
                    dice = dice ?? r;
                }
            }
            if (dice == null || dice.sharedMaterials.Length == 0)
            {
                ShopLog.Debug(() => "Shop terminal model has no dice to replace; keeping the vanilla hologram.");
                return;
            }
            Material glow = dice.sharedMaterials[0];
            Material dark = dice.sharedMaterials.Length > 1 ? dice.sharedMaterials[1] : glow;
            SetDice(container, false);
            Cube = Cube ?? PrimitiveMesh(PrimitiveType.Cube);
            Cylinder = Cylinder ?? PrimitiveMesh(PrimitiveType.Cylinder);

            var root = new GameObject(Name);
            root.layer = container.gameObject.layer;
            root.transform.SetParent(container, false);
            // Where the dice floated. The cart below is drawn with its handle on the left; the
            // offset centres it on the spin axis.
            root.transform.localPosition = new Vector3(0f, 1.0f, 0f);
            root.transform.localScale = Vector3.one * 0.9f;
            var cart = new GameObject("Cart") { layer = root.layer };
            cart.transform.SetParent(root.transform, false);
            cart.transform.localPosition = new Vector3(0.075f, 0f, 0f);
            Build(cart, glow, dark);
        }

        /// <summary>Puts the dice back if this view stops being a shop terminal.</summary>
        public static void Remove(Component view)
        {
            Transform container = view.transform.Find("Container");
            Transform ours = container != null ? container.Find(Name) : null;
            if (ours == null)
            {
                return;
            }
            Object.Destroy(ours.gameObject);
            SetDice(container, true);
        }

        private static void SetDice(Transform container, bool on)
        {
            foreach (Transform child in container)
            {
                if (child.name.StartsWith("Dice"))
                {
                    child.gameObject.SetActive(on);
                }
            }
        }

        /// <summary>
        /// The cart, in the x/y plane with depth along z (it spins, so it's fully 3D): a basket
        /// that's wider at the top, drawn as bright wire bars over dark side panels, a handle up
        /// and to the left, a base rail and four wheels.
        /// </summary>
        private static void Build(GameObject cart, Material glow, Material dark)
        {
            const float side = 0.14f;  // z of each side of the basket
            const float bar = 0.025f;  // frame bar thickness
            const float wire = 0.014f; // basket wire thickness
            // Basket corners (x, y), the same on both sides.
            Vector2 topBack = new Vector2(-0.27f, 0.15f), topFront = new Vector2(0.23f, 0.15f);
            Vector2 bottomBack = new Vector2(-0.19f, -0.07f), bottomFront = new Vector2(0.17f, -0.07f);
            Vector2 grip = new Vector2(-0.38f, 0.24f);

            foreach (float z in new[] { -side, side })
            {
                // Dark side panel behind the wires, then the trapezoid frame.
                Part(cart, Cube, dark, new Vector3(-0.01f, 0.04f, z), Vector3.zero, new Vector3(0.36f, 0.2f, 0.008f));
                Bar(cart, glow, topBack, topFront, z, bar);
                Bar(cart, glow, bottomBack, bottomFront, z, bar);
                Bar(cart, glow, topBack, bottomBack, z, bar);
                Bar(cart, glow, topFront, bottomFront, z, bar);
                // Vertical basket wires.
                foreach (float x in new[] { -0.11f, -0.02f, 0.07f })
                {
                    Bar(cart, glow, new Vector2(x, 0.15f), new Vector2(x, -0.07f), z, wire);
                }
                // A horizontal wire halfway up.
                Bar(cart, glow, new Vector2(-0.23f, 0.04f), new Vector2(0.2f, 0.04f), z, wire);
                // Handle arm, from the basket's back corner up to the grip.
                Bar(cart, glow, topBack, grip, z, bar);
                // Legs and base rail.
                Bar(cart, glow, new Vector2(-0.17f, -0.07f), new Vector2(-0.17f, -0.15f), z, bar);
                Bar(cart, glow, new Vector2(0.15f, -0.07f), new Vector2(0.15f, -0.15f), z, bar);
                Bar(cart, glow, new Vector2(-0.2f, -0.15f), new Vector2(0.18f, -0.15f), z, bar);
                // Wheels: flat discs facing sideways.
                foreach (float x in new[] { -0.15f, 0.13f })
                {
                    Part(cart, Cylinder, glow, new Vector3(x, -0.19f, z), new Vector3(90f, 0f, 0f), new Vector3(0.075f, 0.012f, 0.075f));
                }
            }

            // Bars joining the two sides: basket corners and the handle's grip.
            foreach (Vector2 corner in new[] { topBack, topFront, bottomBack, bottomFront })
            {
                Part(cart, Cube, glow, new Vector3(corner.x, corner.y, 0f), Vector3.zero, new Vector3(bar, bar, side * 2f + bar));
            }
            Part(cart, Cube, glow, new Vector3(grip.x, grip.y, 0f), Vector3.zero, new Vector3(0.035f, 0.035f, side * 2f + 0.06f));
            // Dark floor of the basket.
            Part(cart, Cube, dark, new Vector3(-0.01f, -0.07f, 0f), Vector3.zero, new Vector3(0.36f, 0.008f, side * 2f));
        }

        /// <summary>A bar of square section from a to b, at depth z.</summary>
        private static void Bar(GameObject parent, Material material, Vector2 a, Vector2 b, float z, float thickness)
        {
            Vector2 d = b - a;
            float angle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            Vector2 mid = (a + b) * 0.5f;
            // A little longer than the gap, so bars meet cleanly at the corners.
            Part(parent, Cube, material, new Vector3(mid.x, mid.y, z), new Vector3(0f, 0f, angle),
                new Vector3(d.magnitude + thickness, thickness, thickness));
        }

        private static void Part(GameObject parent, Mesh mesh, Material material, Vector3 position, Vector3 euler, Vector3 size)
        {
            var go = new GameObject("Part") { layer = parent.layer };
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = position;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale = size;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        /// <summary>One of Unity's built-in meshes (taken from a primitive, which is then destroyed).</summary>
        private static Mesh PrimitiveMesh(PrimitiveType type)
        {
            GameObject primitive = GameObject.CreatePrimitive(type);
            Mesh mesh = primitive.GetComponent<MeshFilter>().sharedMesh;
            Object.Destroy(primitive);
            return mesh;
        }
    }
}
