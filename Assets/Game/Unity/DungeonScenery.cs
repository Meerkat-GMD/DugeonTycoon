using System;
using System.Collections.Generic;
using DungeonTycoon.Core;
using UnityEngine;

namespace DungeonTycoon.Unity
{
    internal static class DungeonScenery
    {
        internal const float WallHeight = .95f;

        /// <summary>Floor, walls and props for a stage. Positions come from <paramref name="center"/>.</summary>
        internal static DungeonMesh Dungeon(DungeonMap map, float tile, Func<TilePoint, Vector3> center)
        {
            var m = new DungeonMesh();
            int bossLeft = int.MaxValue, bossRight = int.MinValue, bossTop = int.MaxValue, bossBottom = int.MinValue;
            for (int y = 0; y < map.Height; y++)
            for (int x = 0; x < map.Width; x++)
            {
                var point = new TilePoint(x,y);
                var c = center(point);
                int variation = x*17 + y*31 + x*y*7;
                if (map.IsFloor(point))
                {
                    Finish stone;
                    if (map.IsBossRoom(point))
                    {
                        stone = x == map.BossTile.X ? Finish.Crimson : (x+y)%2 == 0 ? Finish.Slate : Finish.Basalt;
                        bossLeft = Math.Min(bossLeft,x); bossRight = Math.Max(bossRight,x);
                        bossTop = Math.Min(bossTop,y); bossBottom = Math.Max(bossBottom,y);
                    }
                    else stone = variation%5 == 0 ? Finish.Ash : variation%3 == 0 ? Finish.Slate : Finish.Flagstone;
                    m.Box(c.x,-.035f,c.z,tile-.05f,.07f,tile-.05f,stone);
                    if (!map.IsBossRoom(point) && point != map.Entrance && variation%29 == 3)
                        Pebbles(m,c,tile,variation);
                    continue;
                }
                if (!TouchesFloor(map,point)) continue;
                // Dark wall caps keep rooms readable against the lighter flagstones from above.
                m.Box(c.x,WallHeight*.5f-.05f,c.z,tile,WallHeight,tile,Finish.Basalt);
                m.Box(c.x,WallHeight-.03f,c.z,tile-.06f,.08f,tile-.06f,variation%4 == 0 ? Finish.Basalt : Finish.Ink);
                WallCoping(m,map,point,c,tile);
                // Sconces hang on walls that face the viewer across an open floor.
                if (map.IsFloor(new TilePoint(x,y+1)) && variation%6 == 1)
                    Sconce(m,c.x,c.z-tile*.5f);
            }

            var door = center(map.Entrance);
            m.Box(door.x,.005f,door.z,tile*.62f,.04f,tile*.62f,Finish.Teal);
            m.Box(door.x,.03f,door.z,tile*.18f,.04f,tile*.18f,Finish.Mint,0,45,0);
            for (int s = -1; s <= 1; s += 2)
            {
                float px = door.x + s*tile*.38f, pz = door.z + tile*.32f;
                m.Ring(px,.16f,pz,.28f,.22f,.32f,Finish.Slate,6);
                DungeonFigures.Jewel(m,px,.42f,pz,.18f,.32f,Finish.Mint);
            }

            var throne = center(new TilePoint(map.BossTile.X,bossTop));
            Gate(m,throne.x,throne.z+tile*.25f);
            var nearLeft = center(new TilePoint(bossLeft,bossBottom));
            var nearRight = center(new TilePoint(bossRight,bossBottom));
            Torch(m,nearLeft.x,nearLeft.z,.63f);
            Torch(m,nearRight.x,nearRight.z,.63f);
            return m;
        }

        // A pale stone lip follows the room-facing edge, including on foreground walls
        // where the camera sees the cap but hides the vertical face.
        private static void WallCoping(DungeonMesh m, DungeonMap map, TilePoint p, Vector3 c, float tile)
        {
            float width = tile*.1f, offset = (tile-width)*.5f;
            float y = WallHeight+.02f;
            bool north = map.IsFloor(new TilePoint(p.X,p.Y-1));
            bool south = map.IsFloor(new TilePoint(p.X,p.Y+1));
            if (north) m.Box(c.x,y,c.z+offset,tile,.06f,width,Finish.Ash);
            if (south) m.Box(c.x,y,c.z-offset,tile,.06f,width,Finish.Ash);

            float start = c.z-tile*.5f+(south ? width : 0);
            float end = c.z+tile*.5f-(north ? width : 0);
            if (map.IsFloor(new TilePoint(p.X-1,p.Y)))
                m.Box(c.x-offset,y,(start+end)*.5f,width,.06f,end-start,Finish.Ash);
            if (map.IsFloor(new TilePoint(p.X+1,p.Y)))
                m.Box(c.x+offset,y,(start+end)*.5f,width,.06f,end-start,Finish.Ash);
        }

        /// <summary>Outline around floor that is too close to the entrance for monsters.</summary>
        internal static DungeonMesh EntranceZone(DungeonMap map, float tile, Func<TilePoint, Vector3> center, Func<TilePoint, bool> blocked)
        {
            var m = new DungeonMesh();
            var steps = new[] { new TilePoint(0,-1), new TilePoint(1,0), new TilePoint(0,1), new TilePoint(-1,0) };
            for (int y = 0; y < map.Height; y++)
            for (int x = 0; x < map.Width; x++)
            {
                var point = new TilePoint(x,y);
                if (!map.IsFloor(point) || !blocked(point)) continue;
                var c = center(point);
                m.Box(c.x,.012f,c.z,tile*.16f,.02f,tile*.16f,Finish.Ember,0,45,0);
                foreach (var step in steps)
                {
                    var next = new TilePoint(x+step.X,y+step.Y);
                    if (!map.IsFloor(next) || blocked(next)) continue;
                    // Rows grow southward while world z grows northward.
                    float ex = c.x + step.X*tile*.46f, ez = c.z - step.Y*tile*.46f;
                    bool alongX = step.X == 0;
                    m.Box(ex,.02f,ez,alongX ? tile*.9f : .07f,.04f,alongX ? .07f : tile*.9f,Finish.Ember);
                }
            }
            return m;
        }

        /// <summary>
        /// A dashed line along a patrol's loop, with a chevron every few steps pointing the way the squad walks. A leg it
        /// walks both ways is dashed once.
        /// </summary>
        internal static DungeonMesh PatrolRoute(IReadOnlyList<TilePoint> route, float tile, Func<TilePoint, Vector3> center)
        {
            var m = new DungeonMesh();
            const float y = .02f;
            var dashed = new HashSet<(TilePoint, TilePoint)>();
            var marked = new HashSet<TilePoint>();
            for (int i = 0; i < route.Count; i++)
            {
                TilePoint a = route[i], b = route[(i+1)%route.Count];
                if (a == b) continue;
                Vector3 from = center(a), to = center(b);
                if (!dashed.Contains((b,a)) && dashed.Add((a,b)))
                    Stroke(m, Vector3.Lerp(from,to,.24f), Vector3.Lerp(from,to,.76f), tile*.09f, y);
                if (i%3 != 0 || !marked.Add(a)) continue;
                var ahead = (to-from).normalized;
                var side = new Vector3(ahead.z,0,-ahead.x);
                var tip = from + ahead*tile*.13f;
                var back = from - ahead*tile*.07f;
                Stroke(m, back + side*tile*.17f, tip, tile*.07f, y);
                Stroke(m, back - side*tile*.17f, tip, tile*.07f, y);
            }
            return m;
        }

        private static void Stroke(DungeonMesh m, Vector3 from, Vector3 to, float width, float y)
        {
            var span = to-from;
            span.y = 0;
            if (span.sqrMagnitude < .0001f) return;
            var mid = (from+to)*.5f;
            m.Box(mid.x,y,mid.z,width,.012f,span.magnitude,Finish.Ink,0,Mathf.Atan2(span.x,span.z)*Mathf.Rad2Deg,0);
        }

        /// <summary>A lid over every tile the party has not seen, with walls where it meets seen ground.</summary>
        internal static DungeonMesh Fog(DungeonMap map, float tile, Func<TilePoint, Vector3> center, Func<TilePoint, bool> seen)
        {
            var m = new DungeonMesh();
            float top = WallHeight + .06f, half = tile*.5f;
            var steps = new[] { new TilePoint(0,-1), new TilePoint(1,0), new TilePoint(0,1), new TilePoint(-1,0) };
            for (int y = 0; y < map.Height; y++)
            for (int x = 0; x < map.Width; x++)
            {
                var point = new TilePoint(x,y);
                if (seen(point)) continue;
                var c = center(point);
                m.Face(Finish.Ink,
                    new Vector3(c.x-half,top,c.z-half), new Vector3(c.x-half,top,c.z+half),
                    new Vector3(c.x+half,top,c.z+half), new Vector3(c.x+half,top,c.z-half));
                foreach (var step in steps)
                {
                    var next = new TilePoint(x+step.X,y+step.Y);
                    if (!map.Contains(next) || !seen(next)) continue;
                    // Rows grow southward while world z grows northward.
                    var a = new Vector3(c.x + step.X*half - step.Y*half, 0, c.z - step.Y*half - step.X*half);
                    var b = new Vector3(c.x + step.X*half + step.Y*half, 0, c.z - step.Y*half + step.X*half);
                    m.Face(Finish.Ink, a, b, b+Vector3.up*top, a+Vector3.up*top);
                }
            }
            return m;
        }

        private static bool TouchesFloor(DungeonMap map, TilePoint point)
        {
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
                if (map.IsFloor(new TilePoint(point.X+dx,point.Y+dy))) return true;
            return false;
        }

        private static void Pebbles(DungeonMesh m, Vector3 c, float tile, int seed)
        {
            for (int i = 0; i < 3; i++)
            {
                float a = seed*.37f + i*2.1f;
                float r = tile*(.12f+.1f*i);
                m.Box(c.x+Mathf.Cos(a)*r,.04f,c.z+Mathf.Sin(a)*r,.13f-.025f*i,.09f,.11f,
                    i == 1 ? Finish.Ash : Finish.Basalt,0,seed%90+i*31,0);
            }
        }

        private static void Sconce(DungeonMesh m, float x, float z)
        {
            m.Box(x,.62f,z-.04f,.10f,.22f,.09f,Finish.Brass);
            m.Ring(x,.75f,z-.11f,.12f,.2f,.08f,Finish.Brass,6);
            DungeonFigures.Jewel(m,x,.84f,z-.11f,.12f,.24f,Finish.Flame);
        }

        private static void Torch(DungeonMesh m,float x,float z,float height)
        {
            m.Ring(x,.10f,z,.43f,.34f,.2f,Finish.Basalt,6);
            m.Ring(x,height*.54f,z,.19f,.14f,height*.75f,Finish.Brass,6);
            m.Ring(x,height,z,.19f,.42f,.17f,Finish.Brass,8);
            m.Ring(x,height+.09f,z,.39f,.39f,.045f,Finish.Ink,8);
            m.Ring(x,height+.22f,z,.27f,.13f,.27f,Finish.Ember,5);
            DungeonFigures.Jewel(m,x+.025f,height+.35f,z,.15f,.36f,Finish.Flame);
        }

        private static void Gate(DungeonMesh m,float x,float z)
        {
            for (int s = -1; s <= 1; s += 2)
            {
                float post = x+s*.96f;
                m.Box(post,.13f,z,.68f,.26f,.71f,Finish.Ink);
                m.Box(post,.31f,z,.63f,.12f,.62f,Finish.Brass);
                for (int course = 0; course < 3; course++)
                    m.Box(post,.51f+course*.33f,z,.48f,.31f,.51f,course%2 == 0 ? Finish.Slate : Finish.Basalt);
                m.Box(post,1.37f,z,.62f,.15f,.62f,Finish.Brass);
                m.Box(post,1.53f,z,.55f,.17f,.57f,Finish.Slate);
                m.Cutout(post,1.21f,z-.31f,.045f,Finish.Crimson,
                    new Vector2(-.14f,.17f),new Vector2(.14f,.17f),
                    new Vector2(.14f,-.36f),new Vector2(0,-.49f),new Vector2(-.14f,-.36f));
                m.Box(post,1.12f,z-.346f,.127f,.127f,.018f,Finish.Gold,0,0,45);
            }
            const int stones = 9;
            for (int i = 0; i < stones; i++)
            {
                float a = Mathf.PI * (i + .025f) / stones;
                float b = Mathf.PI * (i + .975f) / stones;
                Vector2 innerA = new Vector2(Mathf.Cos(a)*.72f, Mathf.Sin(a)*.64f);
                Vector2 innerB = new Vector2(Mathf.Cos(b)*.72f, Mathf.Sin(b)*.64f);
                Vector2 outerA = new Vector2(Mathf.Cos(a)*1.20f, Mathf.Sin(a)*1.03f);
                Vector2 outerB = new Vector2(Mathf.Cos(b)*1.20f, Mathf.Sin(b)*1.03f);
                m.Cutout(x,1.56f,z,.53f, i == stones/2 ? Finish.Brass : Finish.Slate,
                    innerA,innerB,outerB,outerA);
            }
            DungeonFigures.Jewel(m,x,2.44f,z-.26f,.20f,.28f,Finish.Ember);
        }

        internal static DungeonMesh TileMarker(float tile, Finish paint)
        {
            var m = new DungeonMesh();
            float edge = tile*.47f, arm = tile*.2f;
            for (int sx = -1; sx <= 1; sx += 2)
            for (int sz = -1; sz <= 1; sz += 2)
            {
                m.Box(sx*(edge-arm*.5f),.05f,sz*edge,arm,.035f,.06f,paint);
                m.Box(sx*edge,.05f,sz*(edge-arm*.5f),.06f,.035f,arm,paint);
            }
            return m;
        }
    }
}
