using DungeonTycoon.Core;
using UnityEngine;

namespace DungeonTycoon.Unity
{
    internal static class DungeonFigures
    {
        private static readonly Finish[] Heraldry = { Finish.Teal, Finish.Ivory, Finish.Plum, Finish.Leaf };

        internal static DungeonMesh Hero(int role)
        {
            var m = new DungeonMesh();
            Finish cloth = Heraldry[role];
            Stand(m, .78f, Finish.Teal);
            Boots(m);
            m.Ring(0, .56f, 0, .53f, .36f, .32f, cloth, 7, .79f);
            m.Ring(0, .77f, 0, .36f, .49f, .31f, role == 0 ? Finish.Steel : cloth, 7, .8f);
            m.Ring(0, .64f, 0, .42f, .42f, .065f, Finish.Leather, 8, .88f);
            m.Box(0, .64f, -.18f, .09f, .07f, .045f, Finish.Gold);
            m.Ring(0, 1.035f, -.025f, .29f, .33f, .25f, Finish.Skin, 7, .94f);
            m.Rod(new Vector3(-.18f, .85f, 0), new Vector3(-.31f, .66f, -.1f), .15f, cloth);
            m.Rod(new Vector3(.18f, .84f, 0), new Vector3(.30f, .71f, -.13f), .15f, cloth);
            m.Ring(.30f, .70f, -.14f, .13f, .14f, .10f, Finish.Leather);
            switch (role)
            {
                case 0:
                    m.Ring(0, 1.17f, -.01f, .40f, .23f, .21f, Finish.Steel);
                    m.Box(0, 1.08f, -.178f, .29f, .055f, .06f, Finish.Ink);
                    m.Box(0, 1.04f, -.20f, .044f, .15f, .04f, Finish.Brass);
                    m.Cutout(0, 1.24f, .02f, .13f, Finish.Teal,
                        new Vector2(-.05f, -.05f), new Vector2(.03f, .22f), new Vector2(.13f, .06f));
                    m.Ring(-.24f, .87f, -.01f, .31f, .15f, .19f, Finish.Steel);
                    m.Ring(.24f, .87f, -.01f, .31f, .15f, .19f, Finish.Steel);
                    Shield(m, -.23f, .67f, -.25f, Finish.Brass, .23f, .35f);
                    Shield(m, -.23f, .67f, -.28f, Finish.Teal, .18f, .29f);
                    m.Box(-.23f, .69f, -.31f, .045f, .28f, .028f, Finish.Ivory);
                    Sword(m, .33f, .70f);
                    break;
                case 1:
                    m.Ring(0, 1.17f, 0, .43f, .17f, .28f, Finish.Ivory, 7);
                    m.Box(0, 1.19f, -.16f, .09f, .12f, .045f, Finish.Gold);
                    m.Ring(0, .88f, 0, .54f, .33f, .15f, Finish.Gold, 7, .84f);
                    m.Box(.04f, .67f, -.20f, .075f, .33f, .028f, Finish.Teal);
                    m.Rod(new Vector3(.34f, .14f, -.16f), new Vector3(.34f, 1.42f, -.16f),
                        .055f, Finish.Wood);
                    m.Ring(.34f, 1.36f, -.16f, .21f, .13f, .13f, Finish.Gold);
                    Jewel(m, .34f, 1.52f, -.16f, .19f, .29f, Finish.Mint);
                    break;
                case 2:
                    m.Ring(0, 1.16f, 0, .37f, .26f, .15f, Finish.Leather, 6);
                    m.Box(0, 1.10f, -.158f, .31f, .055f, .06f, Finish.Crimson);
                    m.Rod(new Vector3(-.18f, .88f, -.14f), new Vector3(.14f, .66f, -.14f),
                        .065f, Finish.Brass);
                    m.Ring(-.20f, .86f, -.02f, .26f, .16f, .13f, Finish.Steel);
                    Sword(m, .31f, .72f);
                    Sword(m, -.34f, .60f);
                    break;
                default:
                    m.Ring(0, 1.16f, .02f, .45f, .15f, .27f, Finish.Leaf, 6);
                    m.Cutout(0, .65f, .14f, .08f, Finish.Leaf,
                        new Vector2(-.18f,.23f), new Vector2(.18f,.23f),
                        new Vector2(.29f,-.26f), new Vector2(-.22f,-.33f));
                    var lower = new Vector3(.36f,.36f,-.14f);
                    var upper = new Vector3(.39f,1.29f,-.14f);
                    var grip = new Vector3(.57f,.81f,-.14f);
                    m.Rod(lower, grip, .054f, Finish.Wood);
                    m.Rod(grip, upper, .054f, Finish.Wood);
                    m.Rod(lower, upper, .017f, Finish.Ivory);
                    m.Rod(new Vector3(-.12f,.57f,.15f), new Vector3(-.28f,1.03f,.16f),
                        .13f, Finish.Leather);
                    m.Rod(new Vector3(-.25f,.92f,.16f), new Vector3(-.32f,1.18f,.16f),
                        .055f, Finish.Ivory);
                    break;
            }
            return m;
        }

        private static void Boots(DungeonMesh m)
        {
            m.Box(-.13f,.24f,-.05f,.19f,.24f,.31f,Finish.Leather);
            m.Box(.13f,.24f,-.05f,.19f,.24f,.31f,Finish.Leather);
        }

        private static void Shield(DungeonMesh m, float x, float y, float z, Finish paint, float w, float h)
        {
            m.Cutout(x,y,z,.06f,paint, new Vector2(-w,h*.73f), new Vector2(0,h),
                new Vector2(w,h*.73f), new Vector2(w*.84f,-h*.24f), new Vector2(0,-h),
                new Vector2(-w*.84f,-h*.24f));
        }

        private static void Sword(DungeonMesh m, float x, float y)
        {
            m.Box(x,y-.03f,-.19f,.055f,.25f,.06f,Finish.Wood);
            m.Box(x,y+.09f,-.19f,.22f,.05f,.07f,Finish.Gold);
            m.Cutout(x,y+.09f,-.19f,.05f,Finish.Ivory,
                new Vector2(-.045f,0),new Vector2(-.045f,.34f),new Vector2(0,.46f),
                new Vector2(.045f,.34f),new Vector2(.045f,0));
        }

        // Figure tiers for Monster(). The grave knight, the crypt priest and the necromancer are the bosses of
        // stages 1, 2 and 3.
        internal const int Skeleton = 1, Zombie = 2, Ghost = 3, GraveKnight = 4, Necromancer = 5, Archer = 6, CryptPriest = 7;
        internal const int TierCount = 8;

        internal static bool IsBoss(int tier) => tier == GraveKnight || tier == Necromancer || tier == CryptPriest;

        internal static int TierOf(MonsterKind kind)
        {
            switch (kind)
            {
                case MonsterKind.Ghost: return Ghost;
                case MonsterKind.Zombie: return Zombie;
                case MonsterKind.Archer: return Archer;
                default: return Skeleton;
            }
        }

        internal static int BossTier(int stage) =>
            stage <= 1 ? GraveKnight : stage == 2 ? CryptPriest : Necromancer;

        internal static DungeonMesh Monster(int tier)
        {
            var m = new DungeonMesh();
            bool boss = IsBoss(tier);
            Stand(m, boss ? 1.22f : .90f, boss ? Finish.Gold : Finish.Crimson);
            if (tier == Archer)
            {
                // Skeleton archer: the skeleton's bones under a green hood, burning eyes, a quiver on its back and
                // a bow in its outstretched hand instead of a blade.
                Boots(m);
                m.Ring(0,.55f,0,.40f,.34f,.42f,Finish.Ash,7,.8f);
                m.Ring(0,.85f,-.02f,.36f,.46f,.33f,Finish.Ivory,7);
                m.Ring(0,.72f,.02f,.58f,.42f,.13f,Finish.Leaf,7,.85f);
                m.Ring(0,.90f,.10f,.50f,.50f,.30f,Finish.Leaf,7,.7f);
                m.Ring(0,1.10f,.03f,.50f,0,.36f,Finish.Leaf,7);
                m.Rod(new Vector3(-.16f,.84f,-.17f),new Vector3(.16f,.56f,-.18f),.05f,Finish.Leather);
                for (int s = -1; s <= 1; s += 2)
                    m.Box(s*.10f,.87f,-.21f,.078f,.05f,.045f,Finish.Ember);
                m.Rod(new Vector3(-.18f,.66f,0),new Vector3(-.40f,.72f,-.26f),.09f,Finish.Ivory);
                m.Rod(new Vector3(.18f,.66f,0),new Vector3(.08f,.74f,-.16f),.09f,Finish.Ivory);
                var lower = new Vector3(-.34f,.30f,-.28f);
                var grip = new Vector3(-.50f,.74f,-.28f);
                var upper = new Vector3(-.36f,1.18f,-.28f);
                m.Rod(lower,grip,.05f,Finish.Wood);
                m.Rod(grip,upper,.05f,Finish.Wood);
                m.Rod(lower,upper,.017f,Finish.Ivory);
                m.Ring(grip.x,grip.y,grip.z,.09f,.09f,.08f,Finish.Leather);
                m.Rod(new Vector3(.10f,.50f,.20f),new Vector3(.24f,1.00f,.22f),.13f,Finish.Leather);
                for (int i = 0; i < 2; i++)
                {
                    float x = .19f+.06f*i;
                    m.Rod(new Vector3(x,.95f,.21f),new Vector3(x+.06f,1.17f,.22f),.025f,Finish.Wood);
                    m.Box(x+.065f,1.20f,.22f,.07f,.09f,.03f,Finish.Crimson);
                }
                return m;
            }
            if (tier == CryptPriest)
            {
                // Crypt priest: a bone priest in a long ash robe with a gold mantle, ribs over the chest, a tall
                // mitre, a staff crowned with a halo and a censer smoking green incense.
                m.Ring(0,.55f,0,.98f,.56f,.95f,Finish.Ash,8,.85f);
                m.Ring(0,.13f,0,1.0f,.98f,.08f,Finish.Gold,8,.86f);
                m.Box(0,.45f,-.365f,.14f,.55f,.04f,Finish.Crimson,11.5f);
                for (int i = 0; i < 3; i++)
                    m.Box(0,.95f-.08f*i,-.27f,.30f-.05f*i,.035f,.04f,Finish.Ivory);
                m.Ring(0,1.06f,0,.62f,.44f,.16f,Finish.Gold,8,.85f);
                m.Ring(0,1.27f,-.02f,.36f,.44f,.32f,Finish.Ivory,7);
                for (int s = -1; s <= 1; s += 2)
                    m.Box(s*.10f,1.28f,-.21f,.075f,.055f,.04f,Finish.Mint);
                m.Box(0,1.44f,-.02f,.40f,.06f,.32f,Finish.Gold);
                m.Cutout(0,1.58f,-.02f,.30f,Finish.Ivory,
                    new Vector2(-.18f,-.16f),new Vector2(.18f,-.16f),new Vector2(.15f,.18f),
                    new Vector2(0,.32f),new Vector2(-.15f,.18f));
                m.Box(0,1.62f,-.18f,.09f,.09f,.03f,Finish.Crimson,0,0,45);
                m.Rod(new Vector3(.24f,1.02f,0),new Vector3(.46f,.98f,-.10f),.15f,Finish.Ash);
                m.Ring(.50f,.98f,-.10f,.14f,.14f,.10f,Finish.Ivory);
                m.Rod(new Vector3(.50f,.14f,-.10f),new Vector3(.50f,1.80f,-.10f),.065f,Finish.Wood);
                m.Ring(.50f,1.86f,-.10f,.30f,.30f,.05f,Finish.Brass,8);
                Jewel(m,.50f,1.98f,-.10f,.17f,.28f,Finish.Flame);
                m.Rod(new Vector3(-.24f,1.02f,0),new Vector3(-.44f,.92f,-.20f),.15f,Finish.Ash);
                m.Ring(-.46f,.90f,-.22f,.12f,.12f,.09f,Finish.Ivory);
                m.Rod(new Vector3(-.46f,.86f,-.22f),new Vector3(-.50f,.56f,-.26f),.022f,Finish.Brass);
                m.Ring(-.50f,.47f,-.26f,.12f,.24f,.14f,Finish.Brass,6);
                m.Ring(-.50f,.58f,-.26f,.24f,.06f,.09f,Finish.Brass,6);
                Jewel(m,-.50f,.70f,-.26f,.12f,.20f,Finish.Mint);
                return m;
            }
            if (tier == Skeleton)
            {
                // Skeleton: a bare bone frame with a grey cap, hollow eyes and a rusty blade.
                Boots(m);
                m.Ring(0,.55f,0,.40f,.34f,.42f,Finish.Ash,7,.8f);
                m.Ring(0,.85f,-.02f,.36f,.46f,.33f,Finish.Ivory,7);
                m.Ring(0,1.04f,.005f,.48f,.16f,.19f,Finish.Slate,7);
                for (int s = -1; s <= 1; s += 2)
                {
                    m.Box(s*.10f,.90f,-.20f,.078f,.05f,.045f,Finish.Ink);
                    m.Rod(new Vector3(s*.18f,.64f,0),new Vector3(s*.29f,.55f,-.09f),.09f,Finish.Ivory);
                }
                Sword(m, -.31f, .54f);
                return m;
            }
            if (tier == Ghost)
            {
                // Ghost: a pale shroud that tapers to a wisp above its stand, with glowing eyes.
                m.Ring(0,.38f,0,.10f,.34f,.36f,Finish.Mint,7);
                m.Ring(0,.70f,0,.34f,.44f,.36f,Finish.Ivory,7,.9f);
                m.Ring(0,1.02f,0,.44f,.30f,.30f,Finish.Ivory,7);
                for (int s = -1; s <= 1; s += 2)
                {
                    m.Box(s*.10f,1.02f,-.24f,.07f,.06f,.04f,Finish.Mint);
                    m.Rod(new Vector3(s*.30f,.82f,0),new Vector3(s*.46f,.66f,-.12f),.10f,Finish.Ivory);
                }
                return m;
            }
            if (tier == Zombie)
            {
                // Zombie: a hulking rotting body in torn cloth, sunken eyes and a heavy club.
                Boots(m);
                m.Ring(0,.60f,0,.59f,.76f,.52f,Finish.Leaf,7,.80f);
                m.Ring(0,.46f,0,.59f,.56f,.13f,Finish.Slate,7,.91f);
                m.Ring(0,1.02f,0,.44f,.46f,.31f,Finish.Leaf,6);
                m.Ring(.01f,1.20f,.02f,.48f,.22f,.12f,Finish.Ash,6);
                for (int s = -1; s <= 1; s += 2)
                {
                    m.Ring(s*.33f,.81f,0,.31f,.19f,.20f,Finish.Slate,6);
                    m.Box(s*.13f,1.04f,-.197f,.09f,.048f,.05f,Finish.Ember);
                }
                m.Rod(new Vector3(-.37f,.31f,-.12f),new Vector3(-.37f,1.20f,-.12f),.13f,Finish.Wood);
                return m;
            }
            // The grave knight carries a crown and greaves; the necromancer adds a tattered cloak and a glowing crest.
            bool demon = tier == Necromancer;
            Finish armor = demon ? Finish.Plum : Finish.Basalt;
            Finish trim = demon ? Finish.Ember : Finish.Brass;
            for (int s = -1; s <= 1; s += 2)
            {
                m.Ring(s*.21f,.36f,-.03f,.33f,.27f,.51f,armor,6);
                m.Ring(s*.21f,.23f,-.03f,.35f,.35f,.08f,trim,6);
                m.Ring(s*.39f,.94f,-.01f,.47f,.28f,.27f,armor,6);
                m.Rod(new Vector3(s*.43f,.89f,-.03f),new Vector3(s*.51f,.59f,-.14f),.20f,armor);
                m.Rod(new Vector3(s*.42f,1.00f,0),new Vector3(s*.54f,1.29f,0),.18f,trim,0);
                m.Rod(new Vector3(s*.18f,1.42f,.015f),new Vector3(s*.37f,1.71f,.03f),.16f,trim,0);
                if (demon)
                {
                    m.Cutout(s*.28f,1.11f,.20f,.14f,Finish.Crimson,
                        new Vector2(0,0),new Vector2(s*.74f,.58f),
                        new Vector2(s*.58f,-.12f),new Vector2(s*.16f,-.38f));
                    m.Rod(new Vector3(s*.30f,1.13f,.13f),new Vector3(s*1.02f,1.69f,.14f),
                        .10f,Finish.Plum,.04f);
                }
            }
            m.Ring(0,.76f,0,.51f,.76f,.48f,armor,7,.82f);
            m.Ring(0,.62f,0,.57f,.60f,.12f,trim,7,.88f);
            m.Ring(0,1.04f,0,.70f,.49f,.25f,armor,7,.81f);
            m.Ring(0,1.30f,-.01f,.39f,.49f,.32f,armor,7);
            m.Ring(0,1.48f,-.01f,.50f,.36f,.10f,trim,7);
            for (int s = -1; s <= 1; s += 2)
                m.Box(s*.12f,1.32f,-.22f,.12f,.045f,.045f,Finish.Flame,0,0,s*12);
            Jewel(m,0,.95f,-.26f,.18f,.25f,Finish.Ember);
            if (demon) Jewel(m,0,1.62f,0,.18f,.38f,Finish.Flame);
            return m;
        }

        /// <summary>A treasure chest. An opened one lifts its lid back and shows the empty inside to the camera.</summary>
        internal static DungeonMesh Chest(bool opened)
        {
            var m = new DungeonMesh();
            m.Box(0,.03f,0,.86f,.06f,.60f,Finish.Ink);
            m.Box(0,.22f,0,.76f,.34f,.50f,Finish.Wood);
            m.Box(0,.38f,0,.78f,.04f,.52f,Finish.Brass);
            for (int s = -1; s <= 1; s += 2)
                m.Box(s*.26f,.22f,0,.07f,.36f,.52f,Finish.Brass);
            if (!opened)
            {
                m.Box(0,.47f,0,.76f,.14f,.50f,Finish.Wood);
                m.Box(0,.56f,0,.70f,.06f,.42f,Finish.Wood);
                for (int s = -1; s <= 1; s += 2)
                    m.Box(s*.26f,.50f,0,.07f,.18f,.52f,Finish.Brass);
                m.Box(0,.38f,-.26f,.12f,.14f,.04f,Finish.Gold);
                Jewel(m,0,.66f,0,.10f,.14f,Finish.Gold);
                return m;
            }
            m.Box(0,.40f,0,.64f,.02f,.38f,Finish.Ink);
            // The lid swings up about its back edge and leans past upright.
            const float swing = 110;
            var hinge = new Vector3(0,.40f,.25f);
            var lid = hinge + Quaternion.Euler(swing,0,0)*new Vector3(0,.07f,-.25f);
            m.Box(lid.x,lid.y,lid.z,.76f,.14f,.50f,Finish.Wood,swing);
            m.Box(0,.32f,-.26f,.12f,.10f,.04f,Finish.Brass);
            return m;
        }

        /// <summary>A stone basin fed by a spout. A full one holds glowing water; a dried one shows a cracked bed.</summary>
        internal static DungeonMesh Spring(bool dry)
        {
            var m = new DungeonMesh();
            m.Ring(0,.10f,0,1.0f,.96f,.20f,Finish.Slate,10);
            for (int i = 0; i < 8; i++)
            {
                float a = i*Mathf.PI/4;
                m.Box(Mathf.Cos(a)*.44f,.215f,Mathf.Sin(a)*.44f,.16f,.05f,.10f,Finish.Ash,0,90-i*45,0);
            }
            if (dry)
            {
                m.Ring(0,.205f,0,.76f,.76f,.01f,Finish.Basalt,10);
                m.Box(-.08f,.215f,.02f,.36f,.012f,.025f,Finish.Ink,0,30,0);
                m.Box(.10f,.215f,-.06f,.28f,.012f,.025f,Finish.Ink,0,-50,0);
            }
            else
            {
                m.Ring(0,.21f,0,.78f,.78f,.02f,Finish.Teal,10);
                m.Ring(0,.222f,0,.42f,.42f,.006f,Finish.Mint,10);
            }
            m.Box(0,.42f,.38f,.22f,.62f,.16f,Finish.Slate);
            m.Box(0,.76f,.38f,.28f,.08f,.20f,Finish.Ash);
            m.Box(0,.58f,.29f,.08f,.06f,.06f,Finish.Brass);
            if (!dry) m.Rod(new Vector3(0,.56f,.27f),new Vector3(0,.22f,.17f),.05f,Finish.Mint);
            Jewel(m,0,.92f,.38f,.14f,.22f,dry ? Finish.Slate : Finish.Mint);
            return m;
        }

        internal static void Stand(DungeonMesh m, float width, Finish band)
        {
            m.Ring(0,.045f,0,width,width,.09f,Finish.Ink,12);
            m.Ring(0,.098f,0,width,width*.92f,.035f,band,12);
            m.Ring(0,.125f,0,width*.92f,width*.87f,.025f,Finish.Basalt,12);
        }

        internal static void Jewel(DungeonMesh m, float x, float y, float z, float w, float h, Finish paint)
        {
            m.Ring(x,y-h*.22f,z,0,w,h*.56f,paint,5);
            m.Ring(x,y+h*.28f,z,w,0,h*.44f,paint,5);
        }
    }
}
