using System;
using System.Collections.Generic;
using DungeonTycoon.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace DungeonTycoon.Unity
{
    public sealed class DungeonBoard : MonoBehaviour
    {
        public const float Tile = 1.25f;
        private const float CameraPitch = 58f;
        private const float CameraDistance = 45f;
        private const float MinZoom = 2.6f;
        private const float ZoomStep = 1.13f;
        private const float FollowZoom = 5.2f;
        private const float RoundSeconds = 1.35f;
        // Small offsets keep heroes who share a tile from standing inside each other.
        private static readonly Vector2[] Huddle =
        {
            new Vector2(.12f,.13f), new Vector2(-.13f,-.12f), new Vector2(-.13f,.13f), new Vector2(.12f,-.13f)
        };

        private readonly List<Mesh> meshes = new List<Mesh>();
        private readonly List<Mesh> stageMeshes = new List<Mesh>();
        private readonly List<Transform> foes = new List<Transform>();
        private readonly List<Vector3> foeHomes = new List<Vector3>();
        private readonly Transform[] heroes = new Transform[4];
        private readonly Vector3[] heroGoals = new Vector3[4];
        private readonly bool[] living = { true, true, true, true };
        private Material[] palette;
        private Material[] fogPalette;
        private Mesh fogMesh;
        private Transform[] models;
        private DungeonMap map;
        private int stage;
        private Transform scenery, entranceZone, fog, hoverAllowed, hoverBlocked;
        private Transform healthMeter, healthFill, missile, die;
        private int battleFoe = -1;
        private float roundStarted = -1000;
        private float adversityStarted = -1000;
        private bool blessing;
        private Camera boardCamera;
        private Rect viewport = new Rect(0,0,1,1);
        private Vector3 focus;
        private float zoom = 8;
        private int lastWidth, lastHeight;

        /// <summary>World units per second; set so a hero crosses one tile per exploration step.</summary>
        public float WalkSpeed { get; set; } = 4f;
        public bool FollowParty { get; private set; }

        /// <summary>Follow mode is only useful up close, so turning it on also zooms in when the view is wider.</summary>
        public void SetFollowParty(bool follow)
        {
            FollowParty = follow;
            if (!follow || !boardCamera) return;
            zoom = Mathf.Min(zoom, FollowZoom);
            ApplyCamera();
        }

        public void Initialize()
        {
            if (palette != null) return;
            palette = MakePalette();
            fogPalette = MakeFogPalette();
            models = new Transform[6];
            boardCamera = Camera.main;
            if (!boardCamera)
            {
                var cameraObject = new GameObject("Dungeon camera");
                cameraObject.transform.SetParent(transform, false);
                boardCamera = cameraObject.AddComponent<Camera>();
                cameraObject.tag = "MainCamera";
            }
            boardCamera.orthographic = true;
            boardCamera.clearFlags = CameraClearFlags.SolidColor;
            boardCamera.backgroundColor = new Color32(14,17,21,255);
            boardCamera.nearClipPlane = .1f;
            boardCamera.farClipPlane = 140;
            ConfigureLight();

            hoverAllowed = Draw("놓을 수 있는 칸", DungeonScenery.TileMarker(Tile, Finish.Mint), meshes);
            hoverBlocked = Draw("놓을 수 없는 칸", DungeonScenery.TileMarker(Tile, Finish.Ember), meshes);
            hoverAllowed.gameObject.SetActive(false);
            hoverBlocked.gameObject.SetActive(false);

            var stock = new GameObject("몬스터 원형");
            stock.transform.SetParent(transform,false);
            stock.SetActive(false);
            foreach (int tier in new[] {1, 2, 4, 5})
            {
                var piece = Draw("Monster " + tier, DungeonFigures.Monster(tier), meshes);
                piece.SetParent(stock.transform,false);
                models[tier] = piece;
            }
            for (int i = 0; i < heroes.Length; i++)
            {
                heroes[i] = Draw(new[] {"탱커","힐러","근딜","원딜"}[i], DungeonFigures.Hero(i), meshes);
                heroes[i].gameObject.AddComponent<HeroView>().SetRole((HeroRole)i);
            }
            CreateFeedback();
        }

        private Transform Draw(string objectName, DungeonMesh geometry, List<Mesh> owner, Transform parent = null, Material[] materials = null)
        {
            var obj = new GameObject(objectName);
            obj.transform.SetParent(parent ? parent : transform, false);
            owner.Add(geometry.Attach(obj, materials ?? palette));
            return obj.transform;
        }

        private void StageLight(string objectName, Vector3 position, Color color, float range)
        {
            var lamp = new GameObject(objectName);
            lamp.transform.SetParent(scenery,false);
            lamp.transform.localPosition = position;
            var light = lamp.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.range = range;
            light.intensity = 2.4f;
            light.shadows = LightShadows.None;
        }

        private static void ConfigureLight()
        {
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.70f,.77f,.86f);
            RenderSettings.ambientEquatorColor = new Color(.52f,.57f,.62f);
            RenderSettings.ambientGroundColor = new Color(.37f,.34f,.32f);
            var key = RenderSettings.sun ? RenderSettings.sun : FindFirstObjectByType<Light>();
            if (!key) return;
            key.transform.rotation = Quaternion.Euler(57,-32,0);
            key.color = new Color(1,.91f,.78f);
            key.intensity = 1.45f;
            key.shadows = LightShadows.Soft;
            key.shadowStrength = .7f;
        }

        // ── Dungeon layout ───────────────────────────────────────────────

        public Vector3 TileCenter(TilePoint tile) =>
            new Vector3((tile.X-(map.Width-1)*.5f)*Tile, 0, ((map.Height-1)*.5f-tile.Y)*Tile);

        /// <summary>Rebuilds the floor when the map changes and redraws every monster figure; the boss is last.</summary>
        public void SetStage(int stage, DungeonMap map, IReadOnlyList<MonsterPlacement> monsters,
            Func<TilePoint,bool> placeable, Func<int,bool> defeated)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (!ReferenceEquals(this.map, map))
            {
                this.map = map;
                BuildScenery(placeable);
                for (int i = 0; i < heroes.Length; i++)
                {
                    heroGoals[i] = HeroSpot(map.Entrance, i);
                    heroes[i].localPosition = heroGoals[i];
                    heroes[i].localRotation = Quaternion.identity;
                }
                ShowWholeDungeon();
            }
            if (this.stage != stage)
            {
                roundStarted = adversityStarted = -1000;
                this.stage = stage;
            }
            foreach (var foe in foes) if (foe) Destroy(foe.gameObject);
            foes.Clear();
            foeHomes.Clear();
            var perTile = new Dictionary<TilePoint,int>();
            foreach (var placement in monsters) perTile[placement.Tile] = perTile.TryGetValue(placement.Tile, out int n) ? n+1 : 1;
            var seen = new Dictionary<TilePoint,int>();
            for (int i = 0; i < monsters.Count; i++)
            {
                var placement = monsters[i];
                seen[placement.Tile] = seen.TryGetValue(placement.Tile, out int k) ? k+1 : 0;
                int count = perTile[placement.Tile];
                float angle = seen[placement.Tile]*Mathf.PI*2/count + .6f;
                var offset = count == 1 ? Vector3.zero : new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*Tile*.27f;
                AddFoe(placement.Kind == MonsterKind.Ogre ? 2 : 1, TileCenter(placement.Tile)+offset, count == 1 ? 1f : .72f);
                if (defeated != null && defeated(i)) foes[i].gameObject.SetActive(false);
            }
            AddFoe(stage > 1 ? 5 : 4, TileCenter(map.BossTile), 1f);
            if (healthMeter) healthMeter.gameObject.SetActive(false);
            battleFoe = -1;
        }

        private void AddFoe(int tier, Vector3 home, float scale)
        {
            var foe = Instantiate(models[tier],transform,false);
            foe.gameObject.SetActive(true);
            foe.localPosition = home;
            foe.localRotation = Quaternion.Euler(0,20,0);
            foe.localScale = Vector3.one*scale;
            foes.Add(foe);
            foeHomes.Add(home);
        }

        private void BuildScenery(Func<TilePoint,bool> placeable)
        {
            if (scenery) Destroy(scenery.gameObject);
            foreach (var mesh in stageMeshes) if (mesh) Destroy(mesh);
            stageMeshes.Clear();
            fog = null;
            scenery = new GameObject("던전").transform;
            scenery.SetParent(transform,false);
            Draw("바닥과 벽", DungeonScenery.Dungeon(map, Tile, TileCenter), stageMeshes, scenery);
            entranceZone = Draw("입구 보호 구역",
                DungeonScenery.EntranceZone(map, Tile, TileCenter, tile => !placeable(tile)), stageMeshes, scenery);
            StageLight("입구 수정빛", TileCenter(map.Entrance)+new Vector3(0,1.3f,.2f), new Color32(113,222,204,255), 4.5f);
            StageLight("왕좌 화로", TileCenter(map.BossTile)+new Vector3(0,1.6f,.4f), new Color32(255,149,73,255), 6.5f);
        }

        public void SetPlanning(bool planning)
        {
            if (entranceZone) entranceZone.gameObject.SetActive(planning);
            if (fog) fog.gameObject.SetActive(!planning);
            if (!planning) SetHover(null,false);
        }

        /// <summary>Dims everything the party has not seen. The GM still sees through it.</summary>
        public void SetFog(Func<TilePoint,bool> seen)
        {
            if (map == null || fogPalette == null) return;
            if (fog) Destroy(fog.gameObject);
            if (fogMesh) Destroy(fogMesh);
            var holder = new List<Mesh>();
            fog = Draw("안개", DungeonScenery.Fog(map, Tile, TileCenter, seen), holder, scenery, fogPalette);
            fogMesh = holder[0];
            var renderer = fog.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        public void SetHover(TilePoint? tile, bool allowed)
        {
            bool visible = tile.HasValue && map != null && map.Contains(tile.Value);
            hoverAllowed.gameObject.SetActive(visible && allowed);
            hoverBlocked.gameObject.SetActive(visible && !allowed);
            if (visible) (allowed ? hoverAllowed : hoverBlocked).localPosition = TileCenter(tile.Value);
        }

        public bool TryPickTile(Vector2 screenPosition, out TilePoint tile)
        {
            tile = default;
            if (map == null || !TryFloorPoint(screenPosition, out var point)) return false;
            int x = Mathf.RoundToInt(point.x/Tile + (map.Width-1)*.5f);
            int y = Mathf.RoundToInt((map.Height-1)*.5f - point.z/Tile);
            tile = new TilePoint(x,y);
            return map.Contains(tile);
        }

        // ── Camera ───────────────────────────────────────────────────────

        public void SetViewport(Rect normalizedRect)
        {
            if (normalizedRect.width <= 0 || normalizedRect.height <= 0 || viewport == normalizedRect) return;
            viewport = normalizedRect;
            if (boardCamera) ApplyCamera();
        }

        public bool ContainsScreenPoint(Vector2 screenPosition) =>
            boardCamera && boardCamera.pixelRect.Contains(screenPosition);

        public void ShowWholeDungeon()
        {
            focus = Vector3.zero;
            zoom = float.MaxValue;
            ApplyCamera();
        }

        public void Pan(Vector2 fromScreen, Vector2 toScreen)
        {
            if (!TryFloorPoint(fromScreen, out var from) || !TryFloorPoint(toScreen, out var to)) return;
            FollowParty = false;
            focus += from - to;
            ApplyCamera();
        }

        /// <summary>Positive steps zoom out. The floor point under the cursor stays put.</summary>
        public void ZoomAt(Vector2 screenPosition, float steps)
        {
            bool anchored = TryFloorPoint(screenPosition, out var before);
            zoom *= Mathf.Pow(ZoomStep, steps);
            ApplyCamera();
            if (!anchored || FollowParty || !TryFloorPoint(screenPosition, out var after)) return;
            focus += before - after;
            ApplyCamera();
        }

        private float MaxZoom()
        {
            if (map == null) return 8;
            float aspect = (float)Math.Max(1,boardCamera.pixelWidth)/Math.Max(1,boardCamera.pixelHeight);
            float width = (map.Width+1)*Tile, depth = (map.Height+1)*Tile;
            float sin = Mathf.Sin(CameraPitch*Mathf.Deg2Rad);
            // Walls add a little height at the far edge of the board.
            return Mathf.Max(MinZoom, width/(2*aspect), (depth*sin + DungeonScenery.WallHeight*Mathf.Cos(CameraPitch*Mathf.Deg2Rad))*.5f);
        }

        private void ApplyCamera()
        {
            lastWidth = Screen.width;
            lastHeight = Screen.height;
            boardCamera.rect = viewport;
            zoom = Mathf.Clamp(zoom, MinZoom, MaxZoom());
            if (map != null)
            {
                float aspect = (float)Math.Max(1,boardCamera.pixelWidth)/Math.Max(1,boardCamera.pixelHeight);
                float sin = Mathf.Sin(CameraPitch*Mathf.Deg2Rad);
                // Keep the view over the dungeon, with a little slack past each edge.
                float slack = Tile*2;
                float spanX = Mathf.Max(0, map.Width*Tile*.5f - zoom*aspect + slack);
                float spanZ = Mathf.Max(0, map.Height*Tile*.5f - zoom/sin + slack);
                focus.x = Mathf.Clamp(focus.x,-spanX,spanX);
                focus.z = Mathf.Clamp(focus.z,-spanZ,spanZ);
            }
            focus.y = 0;
            boardCamera.orthographicSize = zoom;
            boardCamera.transform.rotation = transform.rotation*Quaternion.Euler(CameraPitch,0,0);
            boardCamera.transform.position = transform.TransformPoint(focus) - boardCamera.transform.forward*CameraDistance;
        }

        private bool TryFloorPoint(Vector2 screenPosition, out Vector3 local)
        {
            local = default;
            if (!boardCamera) return false;
            var floor = new Plane(transform.up, transform.position);
            var ray = boardCamera.ScreenPointToRay(screenPosition);
            if (!floor.Raycast(ray,out float distance)) return false;
            local = transform.InverseTransformPoint(ray.GetPoint(distance));
            return true;
        }

        // ── Party ────────────────────────────────────────────────────────

        private Vector3 HeroSpot(TilePoint tile, int hero) =>
            TileCenter(tile) + new Vector3(Huddle[hero].x,0,Huddle[hero].y)*Tile;

        /// <summary>Sends each hero toward its tile; they walk there at <see cref="WalkSpeed"/>.</summary>
        public void SetParty(IReadOnlyList<TilePoint> tiles, bool[] alive)
        {
            if (map == null) return;
            for (int i = 0; i < heroes.Length; i++)
            {
                living[i] = alive == null || i >= alive.Length || alive[i];
                if (tiles != null && i < tiles.Count && living[i]) heroGoals[i] = HeroSpot(tiles[i], i);
            }
        }

        public void SetEnemyHealth(int foe, float fraction)
        {
            if (foe < 0 || foe >= foes.Count || !foes[foe]) return;
            fraction = Mathf.Clamp01(fraction);
            foes[foe].gameObject.SetActive(fraction > 0f);
            healthMeter.gameObject.SetActive(fraction > 0f);
            healthMeter.localPosition = foeHomes[foe]+Vector3.up*(foe == foes.Count-1 ? 2.3f : 1.7f);
            healthFill.localScale = new Vector3(fraction,1,1);
            healthFill.localPosition = new Vector3((fraction-1)*.465f,0,-.03f);
            battleFoe = fraction > 0 ? foe : -1;
        }

        public void PlayRound(int foe)
        {
            if (foe < 0 || foe >= foes.Count) return;
            battleFoe = foe;
            roundStarted = Time.time;
        }

        public void PlayAdversity(bool success)
        {
            blessing = success;
            adversityStarted = Time.time;
        }

        private void CreateFeedback()
        {
            var back = new DungeonMesh();
            back.Box(0,0,0,1f,.13f,.035f,Finish.Ink);
            healthMeter = Draw("적 체력",back,meshes);
            var fill = new DungeonMesh();
            fill.Box(0,0,0,.93f,.075f,.032f,Finish.Ember);
            healthFill = Draw("남은 체력",fill,meshes);
            healthFill.SetParent(healthMeter,false);
            healthMeter.gameObject.SetActive(false);

            var arrow = new DungeonMesh();
            arrow.Rod(Vector3.left*.20f, Vector3.right*.14f,.046f,Finish.Ivory);
            arrow.Rod(Vector3.right*.08f,Vector3.right*.27f,.105f,Finish.Mint,0);
            missile = Draw("화살",arrow,meshes);
            missile.gameObject.SetActive(false);
            die = Draw("역경 d20",Die(),meshes);
            die.gameObject.SetActive(false);
        }

        private static DungeonMesh Die()
        {
            var m = new DungeonMesh();
            var lower = new Vector3[5];
            var upper = new Vector3[5];
            for (int i=0;i<5;i++)
            {
                float a = i*2*Mathf.PI/5;
                lower[i] = new Vector3(Mathf.Cos(a)*.32f,-.16f,Mathf.Sin(a)*.32f);
                upper[i] = new Vector3(Mathf.Cos(a+Mathf.PI/5)*.32f,.16f,Mathf.Sin(a+Mathf.PI/5)*.32f);
            }
            void Facet(Vector3 a, Vector3 b, Vector3 c, Finish paint)
            {
                if (Vector3.Dot(Vector3.Cross(b-a,c-a),a+b+c) > 0) m.Triangle(paint,a,b,c);
                else m.Triangle(paint,c,b,a);
            }
            for (int i=0;i<5;i++)
            {
                int j = (i+1)%5;
                Facet(Vector3.down*.36f,lower[i],lower[j],Finish.Brass);
                Facet(lower[i],lower[j],upper[i],Finish.Gold);
                Facet(lower[j],upper[i],upper[j],Finish.Brass);
                Facet(upper[i],upper[j],Vector3.up*.36f,Finish.Gold);
            }
            return m;
        }

        // ── Animation ────────────────────────────────────────────────────

        private void Update()
        {
            if (!boardCamera || healthMeter == null || map == null) return;
            if (lastWidth != Screen.width || lastHeight != Screen.height) ApplyCamera();

            float age = Time.time-roundStarted;
            bool attacking = age >= 0 && age < RoundSeconds && battleFoe >= 0;
            Vector3 foePosition = battleFoe >= 0 && battleFoe < foes.Count ? foeHomes[battleFoe] : Vector3.zero;
            int leaderIndex = -1;
            for (int i = 0; i < heroes.Length; i++)
            {
                Transform hero = heroes[i];
                if (!living[i])
                {
                    var fallen = Quaternion.Euler(0,hero.localEulerAngles.y,0)*Quaternion.Euler(4,0,77);
                    hero.localRotation = Quaternion.RotateTowards(hero.localRotation,fallen,Time.deltaTime*220);
                    continue;
                }
                if (leaderIndex < 0) leaderIndex = i;
                var goal = heroGoals[i];
                Vector3 toward = goal-hero.localPosition;
                toward.y = 0;
                float travel = toward.magnitude;
                float beat = Mathf.Sin(Time.time*12f + i*1.47f);
                float pulse = attacking ? Mathf.Sin(Mathf.Clamp01((age-i*.12f)/.48f)*Mathf.PI) : 0;
                Vector3 lunge = Vector3.zero;
                if (battleFoe >= 0 && (i == 0 || i == 2))
                {
                    var reach = foePosition-goal;
                    reach.y = 0;
                    lunge = reach.normalized*pulse*.45f;
                }
                var target = goal+lunge+Vector3.up*(Mathf.Abs(beat)*Mathf.Clamp01(travel*2)*.1f + pulse*.1f);
                hero.localPosition = Vector3.MoveTowards(hero.localPosition,target,Time.deltaTime*WalkSpeed*1.25f);

                Vector3 facing = travel > .05f ? toward : battleFoe >= 0 ? foePosition-hero.localPosition : Vector3.zero;
                facing.y = 0;
                var heading = facing.sqrMagnitude > .0001f
                    ? Quaternion.LookRotation(-facing.normalized, Vector3.up)
                    : Quaternion.Euler(0,hero.localEulerAngles.y,0);
                var rotation = heading*Quaternion.Euler(0,0,beat*Mathf.Clamp01(travel)*5 - pulse*13);
                hero.localRotation = Quaternion.RotateTowards(hero.localRotation,rotation,Time.deltaTime*360);
            }

            Vector3 leader = heroes[Math.Max(0,leaderIndex)].localPosition;
            if (FollowParty)
            {
                var party = Vector3.zero;
                int count = 0;
                for (int i = 0; i < heroes.Length; i++)
                {
                    if (!living[i]) continue;
                    party += heroes[i].localPosition;
                    count++;
                }
                if (count > 0) focus = Vector3.Lerp(focus, party/count, 1-Mathf.Exp(-Time.deltaTime*4));
                ApplyCamera();
            }

            if (battleFoe >= 0 && battleFoe < foes.Count && foes[battleFoe])
            {
                var foe = foes[battleFoe];
                float counter = attacking ? Mathf.Sin(Mathf.Clamp01((age-.75f)/.46f)*Mathf.PI) : 0;
                var towardParty = leader-foeHomes[battleFoe];
                towardParty.y = 0;
                foe.localPosition = foeHomes[battleFoe]+towardParty.normalized*counter*.5f+Vector3.up*counter*.15f;
                if (towardParty.sqrMagnitude > .0001f)
                    foe.localRotation = Quaternion.LookRotation(-towardParty.normalized,Vector3.up)*Quaternion.Euler(0,0,counter*16);
            }
            bool shoot = attacking && living[3] && age > .46f && age < .78f;
            if (missile.gameObject.activeSelf != shoot) missile.gameObject.SetActive(shoot);
            if (shoot)
            {
                float t = Mathf.InverseLerp(.46f,.78f,age);
                Vector3 start = heroes[3].localPosition + Vector3.up*.9f;
                Vector3 end = foePosition + Vector3.up*.86f;
                missile.localPosition = Vector3.Lerp(start,end,t)+Vector3.up*(.26f*Mathf.Sin(t*Mathf.PI));
                missile.localRotation = Quaternion.FromToRotation(Vector3.right,end-start);
            }
            if (healthMeter.gameObject.activeSelf) healthMeter.rotation = boardCamera.transform.rotation;
            float diceAge = Time.time-adversityStarted;
            bool rolling = diceAge >= 0 && diceAge < 1.5f;
            if (die.gameObject.activeSelf != rolling) die.gameObject.SetActive(rolling);
            if (rolling)
            {
                float t = diceAge/1.5f;
                float lift = Mathf.Sin(Mathf.PI*t);
                die.localPosition = leader+new Vector3(0,1.9f+1.02f*lift,-.4f);
                die.localRotation = Quaternion.Euler(t*320,t*480,blessing ? t*180 : -t*180);
                die.localScale = Vector3.one*Mathf.Clamp01((1-t)*6);
            }
        }

        private static Material[] MakePalette()
        {
            string[] colors =
            {
                "20242B","414B58","596674","77818A","87919A","E9DFC3","90704B",
                "D5AA60","76583F","6D4738","9CB0BC","287777","66D5C0","688957",
                "60495E","9E4751","EA7745","FFBE6E","D6A77E"
            };
            Material source = Resources.Load<Material>("DungeonMiniature");
            var materials = new Material[colors.Length];
            for (int i = 0; i < materials.Length; i++)
            {
                ColorUtility.TryParseHtmlString("#"+colors[i],out var color);
                var mat = new Material(source) { name = ((Finish)i).ToString(), color = color };
                mat.SetFloat("_Smoothness",.26f);
                if (i == (int)Finish.Brass || i == (int)Finish.Gold || i == (int)Finish.Steel)
                    mat.SetFloat("_Metallic",.33f);
                if (i == (int)Finish.Mint || i == (int)Finish.Flame || i == (int)Finish.Ember)
                {
                    mat.EnableKeyword("_EMISSION");
                    mat.SetColor("_EmissionColor",color.linear*.55f);
                }
                materials[i] = mat;
            }
            return materials;
        }

        /// <summary>A see-through unlit material for the fog lid; null when the URP unlit shader is unavailable.</summary>
        private static Material[] MakeFogPalette()
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (!shader) return null;
            var mat = new Material(shader) { name = "Fog" };
            mat.SetFloat("_Surface",1);
            mat.SetFloat("_Blend",0);
            mat.SetFloat("_SrcBlend",(float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend",(float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_SrcBlendAlpha",(float)BlendMode.One);
            mat.SetFloat("_DstBlendAlpha",(float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite",0);
            mat.SetFloat("_Cull",0);
            mat.SetOverrideTag("RenderType","Transparent");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)RenderQueue.Transparent;
            mat.SetColor("_BaseColor",new Color(.03f,.04f,.06f,.58f));
            var materials = new Material[Enum.GetValues(typeof(Finish)).Length];
            for (int i = 0; i < materials.Length; i++) materials[i] = mat;
            return materials;
        }

        private void OnDestroy()
        {
            foreach (var mesh in meshes) if (mesh) Destroy(mesh);
            foreach (var mesh in stageMeshes) if (mesh) Destroy(mesh);
            if (fogMesh) Destroy(fogMesh);
            if (fogPalette != null && fogPalette.Length > 0 && fogPalette[0]) Destroy(fogPalette[0]);
            if (palette == null) return;
            foreach (var material in palette) if (material) Destroy(material);
        }
    }
}
