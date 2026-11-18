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
        // Orthographic view depth only matters for clipping and shadows, so the camera sits just far enough
        // back to keep figures and walls in front of it. URP caps shadow distance at the far plane, which
        // keeps the shadow map spent on the floor the camera can actually see.
        private const float CameraClearance = 4f;
        private const float FarSlack = 2f;
        private const float MinZoom = 2.6f;
        private const float ZoomStep = 1.13f;
        private const float FollowZoom = 5.2f;
        private const float LungeSeconds = .32f;
        private const float FlightSeconds = .3f;
        // Small offsets keep figures who share a tile from standing inside each other.
        private static readonly Vector2[] Huddle =
        {
            new Vector2(.12f,.13f), new Vector2(-.13f,-.12f), new Vector2(-.13f,.13f), new Vector2(.12f,-.13f)
        };

        private sealed class Foe
        {
            public Transform Body, Bar, Fill;
            public Vector3 Goal;
            public bool Alive = true, Engaged;
            public float LungeAt = -1000;
            public Vector3 LungeDirection;
            public float Scale = 1;
            // A pattern's wind-up or landing pose for this frame; cleared once applied.
            public Vector3 PoseOffset;
            public float PoseScale = 1;
            // A skeleton archer shoots its plain blows instead of lunging.
            public bool Ranged;
        }

        private sealed class Flight
        {
            public Transform Body;
            public Vector3 From, To;
            public float Start;
            public Stack<Transform> Pool;
            public bool Spin;
        }

        /// <summary>A chest or spring fixed on the map: drawn fresh until opened or drunk dry, then spent.</summary>
        private sealed class Feature
        {
            public Transform Fresh, Spent;
            public bool IsSpent;
            public float CueAt = -1000;
        }

        private readonly List<Mesh> meshes = new List<Mesh>();
        private readonly List<Mesh> stageMeshes = new List<Mesh>();
        private readonly List<Foe> foes = new List<Foe>();
        private readonly List<Flight> flights = new List<Flight>();
        private readonly Stack<Transform> arrows = new Stack<Transform>();
        private readonly Stack<Transform> orbs = new Stack<Transform>();
        private readonly Stack<Transform> bolts = new Stack<Transform>();
        private readonly List<Feature> chestProps = new List<Feature>();
        private readonly List<Feature> springProps = new List<Feature>();
        // Patrol routes are painted on the floor in one colour per squad: clear while the GM plans around them,
        // faint during the expedition so the GM can still read where a squad is heading.
        private static readonly Color[] PatrolColors = { new Color(.74f,.55f,1f,1), new Color(.45f,.75f,1f,1) };
        private const float PatrolPlanningAlpha = .6f, PatrolExpeditionAlpha = .2f;
        private readonly List<Material> patrolMaterials = new List<Material>();
        private Transform chestPrototype, openChestPrototype, springPrototype, drySpringPrototype, haloPrototype;
        private bool planningView = true;
        private readonly Transform[] heroes = new Transform[4];
        private readonly Animator[] heroAnimators = new Animator[4];
        private static readonly string[] HeroModels = { "Lion", "Rabbit", "Bear", "Crow" };
        private static readonly string[] HeroActions = { "Block", "Heal", "Slash", "Shoot" };
        private static readonly int IdleParameter = Animator.StringToHash("PreviewIdle");
        private readonly Vector3[] heroGoals = new Vector3[4];
        private readonly float[] heroLungeAt = { -1000, -1000, -1000, -1000 };
        private readonly Vector3[] heroLungeDirection = new Vector3[4];
        private readonly bool[] living = { true, true, true, true };
        private Material[] palette;
        private Material[] fogPalette;
        private Mesh fogMesh;
        private Transform[] models;
        private DungeonMap map;
        private int stage;
        private Transform scenery, entranceZone, fog, hoverAllowed, hoverBlocked;
        private Transform barPrototype, arrowPrototype, boltPrototype, orbPrototype, die;
        private float tauntAt = -1000;
        private float adversityStarted = -1000;
        private bool blessing;
        private Camera boardCamera;
        // The part of the screen the HUD leaves uncovered, as a fraction of the screen (origin bottom-left).
        private Rect clearArea = new Rect(0,0,1,1);
        private Vector3 focus;
        private float zoom = 8;
        private int lastWidth, lastHeight;

        /// <summary>World units per second; set so a hero crosses one tile per move.</summary>
        public float WalkSpeed { get; set; } = 4f;
        private Transform heroMarker;
        private int selectedHero = -1;

        /// <summary>Shows a ring under the hero the GM picked for help; -1 hides it.</summary>
        public void SetSelectedHero(int hero)
        {
            selectedHero = hero >= 0 && hero < heroes.Length ? hero : -1;
            if (heroMarker) heroMarker.gameObject.SetActive(selectedHero >= 0);
        }

        /// <summary>The living hero drawn under the cursor, judged on screen around the figure's body.</summary>
        public bool TryPickHero(Vector2 screenPosition, out int hero)
        {
            hero = -1;
            if (!boardCamera) return false;
            float pixelsPerUnit = boardCamera.pixelHeight/(2*boardCamera.orthographicSize);
            float best = Tile*.55f*pixelsPerUnit;
            for (int i = 0; i < heroes.Length; i++)
            {
                if (!living[i]) continue;
                var body = boardCamera.WorldToScreenPoint(transform.TransformPoint(heroes[i].localPosition+Vector3.up*.6f));
                float distance = Vector2.Distance(screenPosition, new Vector2(body.x, body.y));
                if (distance >= best) continue;
                best = distance;
                hero = i;
            }
            return hero >= 0;
        }
        /// <summary>Screen point (origin bottom-left) just above a monster figure; false when it is not drawn.</summary>
        public bool TryFoeScreenPoint(int id, out Vector2 point)
        {
            point = default;
            if (!boardCamera || id < 0 || id >= foes.Count || !foes[id].Body) return false;
            var body = boardCamera.WorldToScreenPoint(transform.TransformPoint(foes[id].Body.localPosition+Vector3.up*.9f));
            point = new Vector2(body.x, body.y);
            return body.z > 0;
        }

        /// <summary>Screen point (origin bottom-left) just above a hero figure.</summary>
        public bool TryHeroScreenPoint(int hero, out Vector2 point)
        {
            point = default;
            if (!boardCamera || hero < 0 || hero >= heroes.Length || !heroes[hero]) return false;
            var body = boardCamera.WorldToScreenPoint(transform.TransformPoint(heroes[hero].localPosition+Vector3.up*1.2f));
            point = new Vector2(body.x, body.y);
            return body.z > 0;
        }

        /// <summary>Screen point (origin bottom-left) above the middle of the standing heroes.</summary>
        public bool TryPartyScreenPoint(out Vector2 point)
        {
            point = default;
            if (!boardCamera) return false;
            Vector3 sum = Vector3.zero;
            int count = 0;
            for (int i = 0; i < heroes.Length; i++)
            {
                if (!living[i] || !heroes[i]) continue;
                sum += heroes[i].localPosition;
                count++;
            }
            if (count == 0) return false;
            var body = boardCamera.WorldToScreenPoint(transform.TransformPoint(sum/count+Vector3.up*1.1f));
            point = new Vector2(body.x, body.y);
            return body.z > 0;
        }

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
            models = new Transform[DungeonFigures.TierCount];
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
            ConfigureLight();

            hoverAllowed = Draw("놓을 수 있는 칸", DungeonScenery.TileMarker(Tile, Finish.Mint), meshes);
            hoverBlocked = Draw("놓을 수 없는 칸", DungeonScenery.TileMarker(Tile, Finish.Ember), meshes);
            hoverAllowed.gameObject.SetActive(false);
            hoverBlocked.gameObject.SetActive(false);
            heroMarker = Draw("도움 대상", DungeonScenery.TileMarker(Tile*.8f, Finish.Gold), meshes);
            heroMarker.gameObject.SetActive(false);
            CreateWarnings();
            effects = new PatternEffects(transform, Tile, palette, TileCenter,
                id => id >= 0 && id < foes.Count ? foes[id].Body : null,
                (id, offset, scale) =>
                {
                    if (id < 0 || id >= foes.Count) return;
                    foes[id].PoseOffset = offset;
                    foes[id].PoseScale = scale;
                });

            var stock = new GameObject("원형");
            stock.transform.SetParent(transform,false);
            stock.SetActive(false);
            for (int tier = 1; tier < DungeonFigures.TierCount; tier++)
            {
                var piece = Draw("Monster " + tier, DungeonFigures.Monster(tier), meshes);
                piece.SetParent(stock.transform,false);
                models[tier] = piece;
            }
            chestPrototype = Draw("보물 상자", DungeonFigures.Chest(false), meshes, stock.transform);
            openChestPrototype = Draw("빈 상자", DungeonFigures.Chest(true), meshes, stock.transform);
            springPrototype = Draw("샘물", DungeonFigures.Spring(false), meshes, stock.transform);
            drySpringPrototype = Draw("마른 샘물", DungeonFigures.Spring(true), meshes, stock.transform);
            var halo = new DungeonMesh();
            halo.Ring(0,.01f,0,1.2f,1.2f,.01f,Finish.Ink,16);
            haloPrototype = Draw("순회조 표시", halo, meshes, stock.transform);
            for (int i = 0; i < heroes.Length; i++)
            {
                var stand = new DungeonMesh();
                DungeonFigures.Stand(stand, .78f, Finish.Teal);
                heroes[i] = Draw(new[] {"탱커","힐러","근딜","원딜"}[i], stand, meshes);
                heroes[i].gameObject.AddComponent<HeroView>().SetRole((HeroRole)i);
                var prefab = Resources.Load<GameObject>("MainCharacters/" + HeroModels[i]);
                var model = Instantiate(prefab, heroes[i], false);
                model.transform.localPosition = Vector3.up * .14f;
                model.transform.localRotation = Quaternion.Euler(0, 180, 0);
                heroAnimators[i] = model.GetComponentInChildren<Animator>();
                heroAnimators[i].applyRootMotion = false;
                heroAnimators[i].SetBool(IdleParameter, true);
            }
            CreateFeedback(stock.transform);
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

        // ── Pattern warnings ─────────────────────────────────────────────

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        // Indexed by CastPhase: a beam still aiming, an area fixed and about to land, the flash where it landed.
        private static readonly Color[] WarnColors =
        {
            new Color(1f,.62f,.25f,1), new Color(1f,.22f,.18f,1), new Color(1f,.93f,.72f,1)
        };
        private readonly List<MeshRenderer> warnTiles = new List<MeshRenderer>();
        private MaterialPropertyBlock warnBlock;
        private Material warnMaterial;
        private Mesh warnMesh;
        private Transform warnRoot;
        private PatternEffects effects;

        private void CreateWarnings()
        {
            warnRoot = new GameObject("예고 범위").transform;
            warnRoot.SetParent(transform,false);
            warnBlock = new MaterialPropertyBlock();
            float h = Tile*.46f;
            warnMesh = new Mesh { name = "예고 칸" };
            warnMesh.SetVertices(new[] { new Vector3(-h,0,-h), new Vector3(-h,0,h), new Vector3(h,0,h), new Vector3(h,0,-h) });
            warnMesh.SetTriangles(new[] { 0,1,2, 0,2,3 },0);
            warnMesh.RecalculateNormals();
            warnMaterial = Overlay("Warning") ?? palette[(int)Finish.Ember];
        }

        /// <summary>A see-through unlit material for marks painted over the floor; null without the URP unlit shader.</summary>
        private static Material Overlay(string materialName)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (!shader) return null;
            var mat = new Material(shader) { name = materialName };
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
            return mat;
        }

        /// <summary>The see-through paint for one patrol squad's route and the rings under its members.</summary>
        private Material PatrolMaterial(int squad)
        {
            while (patrolMaterials.Count <= squad)
            {
                var mat = Overlay("Patrol " + (patrolMaterials.Count+1));
                if (!mat)
                {
                    // Without the unlit shader the routes fall back to a solid palette colour.
                    patrolMaterials.Add(palette[(int)Finish.Plum]);
                    continue;
                }
                patrolMaterials.Add(mat);
                PaintPatrol(mat, patrolMaterials.Count-1);
            }
            return patrolMaterials[squad];
        }

        private void PaintPatrol(Material mat, int squad)
        {
            if (Array.IndexOf(palette, mat) >= 0) return;
            var color = PatrolColors[squad % PatrolColors.Length];
            color.a = planningView ? PatrolPlanningAlpha : PatrolExpeditionAlpha;
            mat.SetColor(BaseColorId, color);
        }

        private MeshRenderer WarnTile(int index)
        {
            while (warnTiles.Count <= index)
            {
                var piece = new GameObject("예고 칸");
                piece.transform.SetParent(warnRoot,false);
                piece.AddComponent<MeshFilter>().sharedMesh = warnMesh;
                var renderer = piece.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = warnMaterial;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                warnTiles.Add(renderer);
            }
            var tile = warnTiles[index];
            if (!tile.gameObject.activeSelf) tile.gameObject.SetActive(true);
            return tile;
        }

        /// <summary>
        /// Paints the tiles every telegraphed pattern will hit, exactly the ones the rules will check: orange while a
        /// beam still follows its target, red once fixed and deepening toward the moment it lands, then a brief pale flash.
        /// </summary>
        public void SetCasts(IReadOnlyList<PatternCast> casts, int ticks)
        {
            effects?.Sync(map == null ? null : casts, ticks);
            int used = 0;
            if (map != null && casts != null && warnRoot)
            {
                foreach (var cast in casts)
                {
                    float progress = Mathf.Clamp01((float)(ticks-cast.StartTick)/Mathf.Max(1,cast.FireTick-cast.StartTick));
                    var color = WarnColors[(int)cast.Phase];
                    color.a = cast.Phase == CastPhase.Fired ? .75f : Mathf.Lerp(.22f,.55f,progress);
                    warnBlock.SetColor(BaseColorId,color);
                    foreach (var tile in cast.Area)
                    {
                        var renderer = WarnTile(used++);
                        renderer.SetPropertyBlock(warnBlock);
                        renderer.transform.localPosition = TileCenter(tile)+Vector3.up*(.06f+.004f*(int)cast.Phase);
                    }
                }
            }
            for (int i = used; i < warnTiles.Count; i++)
                if (warnTiles[i].gameObject.activeSelf) warnTiles[i].gameObject.SetActive(false);
        }

        /// <summary>
        /// Rebuilds the floor, chests, springs and patrol routes when the map changes, and redraws the monsters in the
        /// order of <see cref="DungeonRun.Monsters"/>: the placed ones, then each patrol member at its starting tile,
        /// squad by squad, then the stage's boss.
        /// </summary>
        public void SetStage(int stage, DungeonMap map, IReadOnlyList<MonsterPlacement> placements,
            IReadOnlyList<PatrolSquad> patrols, Func<TilePoint,bool> placeable)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (!ReferenceEquals(this.map, map))
            {
                this.map = map;
                BuildScenery(placeable, patrols);
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
                adversityStarted = -1000;
                this.stage = stage;
            }
            ClearFoes();
            var tiles = new List<TilePoint>();
            foreach (var placement in placements)
            {
                AddFoe(DungeonFigures.TierOf(placement.Kind));
                tiles.Add(placement.Tile);
            }
            if (patrols != null)
                foreach (var squad in patrols)
                    for (int k = 0; k < squad.Members.Count; k++)
                    {
                        AddFoe(DungeonFigures.TierOf(squad.Members[k]), squad.Index);
                        tiles.Add(squad.StartOf(k));
                    }
            AddFoe(DungeonFigures.BossTier(stage));
            tiles.Add(map.BossTile);
            PlaceFoes(tiles, null, true);
        }

        private void ClearFoes()
        {
            foreach (var foe in foes)
            {
                if (foe.Body) Destroy(foe.Body.gameObject);
                if (foe.Bar) Destroy(foe.Bar.gameObject);
            }
            foes.Clear();
        }

        /// <summary>Adds a monster figure; a patrol member (<paramref name="squad"/> ≥ 0) stands on its squad's ring.</summary>
        private void AddFoe(int tier, int squad = -1)
        {
            var body = Instantiate(models[tier],transform,false);
            body.gameObject.SetActive(true);
            body.localRotation = Quaternion.Euler(0,20,0);
            if (squad >= 0)
            {
                var halo = Instantiate(haloPrototype,body,false);
                halo.gameObject.SetActive(true);
                var ring = halo.GetComponent<MeshRenderer>();
                ring.sharedMaterial = PatrolMaterial(squad);
                ring.shadowCastingMode = ShadowCastingMode.Off;
                ring.receiveShadows = false;
            }
            var bar = Instantiate(barPrototype,transform,false);
            bar.gameObject.SetActive(false);
            foes.Add(new Foe { Body = body, Bar = bar, Fill = bar.GetChild(0), Ranged = tier == DungeonFigures.Archer });
        }

        /// <summary>Spreads figures that share a tile; stacked monsters are drawn smaller.</summary>
        private void PlaceFoes(IReadOnlyList<TilePoint> tiles, IReadOnlyList<bool> alive, bool snap)
        {
            var perTile = new Dictionary<TilePoint,int>();
            for (int i = 0; i < tiles.Count; i++)
                if (alive == null || alive[i]) perTile[tiles[i]] = perTile.TryGetValue(tiles[i], out int n) ? n+1 : 1;
            var seen = new Dictionary<TilePoint,int>();
            for (int i = 0; i < tiles.Count; i++)
            {
                if (alive != null && !alive[i]) continue;
                seen[tiles[i]] = seen.TryGetValue(tiles[i], out int k) ? k+1 : 0;
                int count = perTile[tiles[i]];
                float angle = seen[tiles[i]]*Mathf.PI*2/count + .6f;
                var offset = count == 1 ? Vector3.zero : new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*Tile*.27f;
                var foe = foes[i];
                foe.Goal = TileCenter(tiles[i])+offset;
                foe.Scale = count == 1 || i == foes.Count-1 ? 1f : .72f;
                if (!snap) continue;
                foe.Body.localPosition = foe.Goal;
                foe.Body.localScale = Vector3.one*foe.Scale;
            }
        }

        /// <summary>Moves monster figures toward their current tiles and updates health bars.</summary>
        public void SetMonsters(IReadOnlyList<MonsterState> monsters)
        {
            if (map == null || monsters == null || monsters.Count != foes.Count) return;
            var tiles = new TilePoint[monsters.Count];
            var alive = new bool[monsters.Count];
            for (int i = 0; i < monsters.Count; i++)
            {
                var monster = monsters[i];
                var foe = foes[i];
                tiles[i] = monster.Tile;
                alive[i] = monster.Alive;
                if (foe.Alive != monster.Alive)
                {
                    foe.Alive = monster.Alive;
                    foe.Body.gameObject.SetActive(monster.Alive);
                }
                foe.Engaged = monster.Alive && monster.Mood == MonsterMood.Chasing;
                bool showBar = monster.Alive && (foe.Engaged || monster.Hp < monster.MaxHp);
                if (foe.Bar.gameObject.activeSelf != showBar) foe.Bar.gameObject.SetActive(showBar);
                float fraction = Mathf.Clamp01((float)monster.Hp/monster.MaxHp);
                foe.Fill.localScale = new Vector3(fraction,1,1);
                foe.Fill.localPosition = new Vector3((fraction-1)*.465f,0,-.03f);
            }
            PlaceFoes(tiles, alive, false);
        }

        private void BuildScenery(Func<TilePoint,bool> placeable, IReadOnlyList<PatrolSquad> patrols)
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
            BuildFeatures(chestProps, map.Chests, chestPrototype, openChestPrototype, "보물 상자");
            BuildFeatures(springProps, map.Springs, springPrototype, drySpringPrototype, "샘물");
            if (patrols == null) return;
            foreach (var squad in patrols)
            {
                var paint = PatrolMaterial(squad.Index);
                var materials = new Material[palette.Length];
                for (int i = 0; i < materials.Length; i++) materials[i] = paint;
                var route = Draw("순회로 "+(squad.Index+1), DungeonScenery.PatrolRoute(squad.Route, Tile, TileCenter),
                    stageMeshes, scenery, materials);
                var renderer = route.GetComponent<MeshRenderer>();
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
        }

        /// <summary>Stands a fresh and a spent copy of a chest or spring on each of its tiles; only one shows.</summary>
        private void BuildFeatures(List<Feature> props, IReadOnlyList<TilePoint> tiles, Transform fresh, Transform spent, string label)
        {
            props.Clear();
            for (int i = 0; i < tiles.Count; i++)
            {
                var prop = new Feature { Fresh = Instantiate(fresh,scenery,false), Spent = Instantiate(spent,scenery,false) };
                prop.Fresh.name = label+" "+(i+1);
                prop.Spent.name = label+" "+(i+1)+" (빈)";
                foreach (var piece in new[] {prop.Fresh, prop.Spent})
                {
                    piece.localPosition = TileCenter(tiles[i]);
                    piece.localRotation = Quaternion.Euler(0,(i%2 == 0 ? -1 : 1)*12,0);
                }
                prop.Fresh.gameObject.SetActive(true);
                prop.Spent.gameObject.SetActive(false);
                props.Add(prop);
            }
        }

        /// <summary>Shows each chest shut or opened and each spring full or dried, as the run has them.</summary>
        public void SetFeatures(IReadOnlyList<TreasureChest> chests, IReadOnlyList<Spring> springs)
        {
            for (int i = 0; i < chestProps.Count; i++)
                Spend(chestProps[i], chests != null && i < chests.Count && chests[i].Opened);
            for (int i = 0; i < springProps.Count; i++)
                Spend(springProps[i], springs != null && i < springs.Count && springs[i].Used);
        }

        private static void Spend(Feature prop, bool spent)
        {
            if (prop.IsSpent == spent) return;
            prop.IsSpent = spent;
            prop.Fresh.gameObject.SetActive(!spent);
            prop.Spent.gameObject.SetActive(spent);
        }

        /// <summary>Screen point (origin bottom-left) just above a chest or spring; false when it is not drawn.</summary>
        public bool TryFeatureScreenPoint(bool spring, int index, out Vector2 point)
        {
            point = default;
            var props = spring ? springProps : chestProps;
            if (!boardCamera || index < 0 || index >= props.Count) return false;
            var body = boardCamera.WorldToScreenPoint(transform.TransformPoint(props[index].Fresh.localPosition+Vector3.up*.8f));
            point = new Vector2(body.x, body.y);
            return body.z > 0;
        }

        public void SetPlanning(bool planning)
        {
            if (entranceZone) entranceZone.gameObject.SetActive(planning);
            if (fog) fog.gameObject.SetActive(!planning);
            if (!planning) SetHover(null,false);
            if (planningView == planning) return;
            planningView = planning;
            for (int i = 0; i < patrolMaterials.Count; i++) PaintPatrol(patrolMaterials[i], i);
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

        private readonly List<Transform> routeMarks = new List<Transform>();

        /// <summary>Lights the tiles of one way at a fork (the oracle card under the mouse); null clears it.</summary>
        public void ShowRoute(IReadOnlyList<TilePoint> tiles)
        {
            int count = tiles?.Count ?? 0;
            for (int i = 0; i < Mathf.Max(count, routeMarks.Count); i++)
            {
                if (i >= routeMarks.Count)
                {
                    var mark = Instantiate(hoverAllowed, transform, false);
                    mark.name = "신탁 길";
                    routeMarks.Add(mark);
                }
                // The first tile is where the party already stands.
                bool on = i > 0 && i < count && map != null && map.Contains(tiles[i]);
                routeMarks[i].gameObject.SetActive(on);
                if (on) routeMarks[i].localPosition = TileCenter(tiles[i]);
            }
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

        /// <summary>
        /// The camera always draws the whole screen and the HUD floats on top. This names the part of the
        /// screen the HUD leaves uncovered, so focus, follow, full zoom-out and pan limits use that area.
        /// The view does not jump when the area changes.
        /// </summary>
        public void SetClearArea(Rect normalizedRect)
        {
            normalizedRect = Rect.MinMaxRect(Mathf.Clamp01(normalizedRect.xMin), Mathf.Clamp01(normalizedRect.yMin),
                Mathf.Clamp01(normalizedRect.xMax), Mathf.Clamp01(normalizedRect.yMax));
            if (normalizedRect.width < .05f || normalizedRect.height < .05f || clearArea == normalizedRect) return;
            if (boardCamera && !FollowParty && TryFloorPoint(
                    new Vector2(normalizedRect.center.x*Screen.width, normalizedRect.center.y*Screen.height), out var kept))
                focus = kept;
            clearArea = normalizedRect;
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
            // Fully zoomed out, the whole dungeon fits the uncovered area. Walls add a little height at the far edge.
            return Mathf.Max(MinZoom, width/(2*aspect*clearArea.width),
                (depth*sin + DungeonScenery.WallHeight*Mathf.Cos(CameraPitch*Mathf.Deg2Rad))*.5f/clearArea.height);
        }

        private void ApplyCamera()
        {
            lastWidth = Screen.width;
            lastHeight = Screen.height;
            boardCamera.rect = new Rect(0,0,1,1);
            zoom = Mathf.Clamp(zoom, MinZoom, MaxZoom());
            float aspect = (float)Math.Max(1,boardCamera.pixelWidth)/Math.Max(1,boardCamera.pixelHeight);
            if (map != null)
            {
                float sin = Mathf.Sin(CameraPitch*Mathf.Deg2Rad);
                // focus sits at the centre of the uncovered area. Every edge of the dungeon can be brought
                // into that area, with a little slack, so no part of it stays trapped under the HUD.
                float slack = Tile*2;
                float spanX = Mathf.Max(0, map.Width*Tile*.5f - zoom*aspect*clearArea.width + slack);
                float spanZ = Mathf.Max(0, map.Height*Tile*.5f - zoom*clearArea.height/sin + slack);
                focus.x = Mathf.Clamp(focus.x,-spanX,spanX);
                focus.z = Mathf.Clamp(focus.z,-spanZ,spanZ);
            }
            focus.y = 0;
            boardCamera.orthographicSize = zoom;
            var view = boardCamera.transform;
            view.rotation = transform.rotation*Quaternion.Euler(CameraPitch,0,0);
            // Shift the camera so focus lands on the centre of the uncovered area, not the screen centre.
            float shiftX = (.5f-clearArea.center.x)*2*zoom*aspect;
            float shiftY = (.5f-clearArea.center.y)*2*zoom;
            // A floor point that sits u above the focus on screen lies u/tan(pitch) further along the view axis.
            // The view's bottom edge is at shiftY - zoom and its top edge at shiftY + zoom, so the nearest floor
            // keeps CameraClearance in front of the camera and the far plane stops just past the farthest floor.
            float tan = Mathf.Tan(CameraPitch*Mathf.Deg2Rad);
            float distance = (zoom-shiftY)/tan + CameraClearance;
            boardCamera.farClipPlane = distance + (zoom+shiftY)/tan + FarSlack;
            view.position = transform.TransformPoint(focus) - view.forward*distance + view.right*shiftX + view.up*shiftY;
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

        // ── Party and combat cues ────────────────────────────────────────

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

        /// <summary>Turns rule events into lunges, arrows, heal orbs and the tank's taunt hop.</summary>
        public void PlayEvents(IReadOnlyList<CombatEvent> events)
        {
            if (events == null) return;
            foreach (var e in events)
            {
                // A chest or spring event names the feature, not a monster.
                if (e.Kind == CombatEventKind.Chest || e.Kind == CombatEventKind.Spring)
                {
                    PlayFeature(e.Kind == CombatEventKind.Spring, e.Source);
                    continue;
                }
                if (e.FromHero && (e.Source < 0 || e.Source >= heroes.Length)) continue;
                if (!e.FromHero && (e.Source < 0 || e.Source >= foes.Count)) continue;
                if (e.FromHero && (e.Kind == CombatEventKind.Attack || e.Kind == CombatEventKind.Heal ||
                                   e.Kind == CombatEventKind.Taunt))
                    heroAnimators[e.Source].SetTrigger(HeroActions[e.Source]);
                Vector3 to = e.ToHero
                    ? (e.Target >= 0 && e.Target < heroes.Length ? heroes[e.Target].localPosition : Vector3.zero)
                    : (e.Target >= 0 && e.Target < foes.Count ? foes[e.Target].Body.localPosition : Vector3.zero);
                switch (e.Kind)
                {
                    case CombatEventKind.Drop:
                    case CombatEventKind.Skill:
                        break;
                    case CombatEventKind.Taunt:
                        tauntAt = Time.time;
                        break;
                    case CombatEventKind.Heal:
                        Launch(heroes[e.Source].localPosition, to, true);
                        break;
                    default:
                        if (!e.FromHero)
                        {
                            var foe = foes[e.Source];
                            // An archer's plain blow is a shot: a bone arrow flies to its target instead of a lunge.
                            if (foe.Ranged && e.Kind == CombatEventKind.Attack)
                            {
                                Launch(foe.Body.localPosition, to, boltPrototype, bolts, false);
                                break;
                            }
                            // A pattern that hits from afar has its own effect; only blows within reach lunge.
                            if (Flat(to-foe.Body.localPosition).magnitude > Tile*1.7f) break;
                            foe.LungeAt = Time.time;
                            foe.LungeDirection = Flat(to-foe.Body.localPosition);
                        }
                        else if (e.Source == (int)HeroRole.Tank || e.Source == (int)HeroRole.MeleeDamage)
                        {
                            heroLungeAt[e.Source] = Time.time;
                            heroLungeDirection[e.Source] = Flat(to-heroes[e.Source].localPosition);
                        }
                        else Launch(heroes[e.Source].localPosition, to, e.Source == (int)HeroRole.Healer);
                        break;
                }
            }
        }

        private static Vector3 Flat(Vector3 v)
        {
            v.y = 0;
            return v.sqrMagnitude > .0001f ? v.normalized : Vector3.zero;
        }

        private void Launch(Vector3 from, Vector3 to, bool orb) =>
            Launch(from, to, orb ? orbPrototype : arrowPrototype, orb ? orbs : arrows, orb);

        private void Launch(Vector3 from, Vector3 to, Transform prototype, Stack<Transform> pool, bool spin)
        {
            var body = pool.Count > 0 ? pool.Pop() : Instantiate(prototype, transform, false);
            body.gameObject.SetActive(true);
            flights.Add(new Flight { Body = body, From = from+Vector3.up*.9f, To = to+Vector3.up*.86f, Start = Time.time,
                Pool = pool, Spin = spin });
        }

        /// <summary>
        /// The opened chest hops as its lid flies up (the HUD shows the item it held); a spring the party drinks
        /// from ripples and sends a mote of light to each standing hero as it runs dry.
        /// </summary>
        private void PlayFeature(bool spring, int index)
        {
            var props = spring ? springProps : chestProps;
            if (index < 0 || index >= props.Count) return;
            var prop = props[index];
            prop.CueAt = Time.time;
            Spend(prop, true);
            if (!spring) return;
            var from = prop.Fresh.localPosition+Vector3.down*.5f;
            for (int i = 0; i < heroes.Length; i++)
                if (living[i]) Launch(from, heroes[i].localPosition, true);
        }

        public void PlayAdversity(bool success)
        {
            blessing = success;
            adversityStarted = Time.time;
        }

        private void CreateFeedback(Transform stock)
        {
            var back = new DungeonMesh();
            back.Box(0,0,0,1f,.13f,.035f,Finish.Ink);
            barPrototype = Draw("체력 막대",back,meshes,stock);
            var fill = new DungeonMesh();
            fill.Box(0,0,0,.93f,.075f,.032f,Finish.Ember);
            Draw("남은 체력",fill,meshes,barPrototype);
            barPrototype.localScale = Vector3.one*.8f;

            var arrow = new DungeonMesh();
            arrow.Rod(Vector3.left*.20f, Vector3.right*.14f,.046f,Finish.Ivory);
            arrow.Rod(Vector3.right*.08f,Vector3.right*.27f,.105f,Finish.Mint,0);
            arrowPrototype = Draw("화살",arrow,meshes,stock);
            var orb = new DungeonMesh();
            DungeonFigures.Jewel(orb,0,0,0,.2f,.3f,Finish.Mint);
            orbPrototype = Draw("치유 빛",orb,meshes,stock);
            var bolt = new DungeonMesh();
            bolt.Rod(Vector3.left*.22f, Vector3.right*.14f,.04f,Finish.Ash);
            bolt.Rod(Vector3.right*.08f,Vector3.right*.27f,.10f,Finish.Ember,0);
            bolt.Box(-.19f,0,0,.09f,.10f,.02f,Finish.Crimson);
            boltPrototype = Draw("해골 화살",bolt,meshes,stock);
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

        private static float Pulse(float startedAt, float seconds)
        {
            float t = (Time.time-startedAt)/seconds;
            return t >= 0 && t < 1 ? Mathf.Sin(t*Mathf.PI) : 0;
        }

        private void Update()
        {
            if (!boardCamera || map == null) return;
            effects?.Animate();
            if (lastWidth != Screen.width || lastHeight != Screen.height) ApplyCamera();

            int leaderIndex = -1;
            for (int i = 0; i < heroes.Length; i++)
            {
                Transform hero = heroes[i];
                if (!living[i])
                {
                    heroAnimators[i].speed = 0;
                    var fallen = Quaternion.Euler(0,hero.localEulerAngles.y,0)*Quaternion.Euler(4,0,77);
                    hero.localRotation = Quaternion.RotateTowards(hero.localRotation,fallen,Time.deltaTime*220);
                    continue;
                }
                if (leaderIndex < 0) leaderIndex = i;
                var goal = heroGoals[i];
                Vector3 toward = goal-hero.localPosition;
                toward.y = 0;
                float travel = toward.magnitude;
                heroAnimators[i].speed = 1;
                heroAnimators[i].SetBool(IdleParameter, travel <= .05f);
                float beat = Mathf.Sin(Time.time*12f + i*1.47f);
                float lunge = Pulse(heroLungeAt[i], LungeSeconds);
                float hop = i == (int)HeroRole.Tank ? Pulse(tauntAt, .45f) : 0;
                var target = goal + heroLungeDirection[i]*lunge*.42f +
                             Vector3.up*(Mathf.Abs(beat)*Mathf.Clamp01(travel*2)*.1f + lunge*.08f + hop*.35f);
                hero.localPosition = Vector3.MoveTowards(hero.localPosition,target,Time.deltaTime*WalkSpeed*1.6f);

                Vector3 facing = travel > .05f ? toward : lunge > 0 ? heroLungeDirection[i] : NearestFoeDirection(hero.localPosition);
                facing.y = 0;
                var heading = facing.sqrMagnitude > .0001f
                    ? Quaternion.LookRotation(-facing.normalized, Vector3.up)
                    : Quaternion.Euler(0,hero.localEulerAngles.y,0);
                var rotation = heading*Quaternion.Euler(0,0,beat*Mathf.Clamp01(travel)*5 - lunge*13);
                hero.localRotation = Quaternion.RotateTowards(hero.localRotation,rotation,Time.deltaTime*360);
            }

            Vector3 leader = heroes[Math.Max(0,leaderIndex)].localPosition;
            if (selectedHero >= 0 && heroMarker.gameObject.activeSelf)
            {
                var spot = heroes[selectedHero].localPosition;
                heroMarker.localPosition = new Vector3(spot.x,0,spot.z);
            }
            foreach (var foe in foes)
            {
                if (!foe.Alive) continue;
                var body = foe.Body;
                float lunge = Pulse(foe.LungeAt, LungeSeconds);
                Vector3 toward = foe.Goal-body.localPosition;
                toward.y = 0;
                var target = foe.Goal + foe.LungeDirection*lunge*.42f + Vector3.up*lunge*.12f + foe.PoseOffset;
                body.localPosition = Vector3.MoveTowards(body.localPosition,target,Time.deltaTime*WalkSpeed*1.4f);
                bool posed = foe.PoseScale != 1 || foe.PoseOffset != Vector3.zero;
                body.localScale = Vector3.MoveTowards(body.localScale,Vector3.one*foe.Scale*foe.PoseScale,Time.deltaTime*(posed ? 6 : 2));
                foe.PoseOffset = Vector3.zero;
                foe.PoseScale = 1;
                Vector3 facing = toward.magnitude > .05f ? toward : foe.Engaged ? NearestHeroDirection(body.localPosition) : Vector3.zero;
                if (facing.sqrMagnitude > .0001f)
                    body.localRotation = Quaternion.RotateTowards(body.localRotation,
                        Quaternion.LookRotation(-Flat(facing),Vector3.up)*Quaternion.Euler(0,0,lunge*16),Time.deltaTime*300);
                if (foe.Bar.gameObject.activeSelf)
                {
                    foe.Bar.localPosition = body.localPosition+Vector3.up*(foe == foes[foes.Count-1] ? 2.3f : 1.6f*foe.Scale+.1f);
                    foe.Bar.rotation = boardCamera.transform.rotation;
                }
            }

            for (int i = flights.Count-1; i >= 0; i--)
            {
                var flight = flights[i];
                float t = (Time.time-flight.Start)/FlightSeconds;
                if (t >= 1)
                {
                    flight.Body.gameObject.SetActive(false);
                    flight.Pool.Push(flight.Body);
                    flights.RemoveAt(i);
                    continue;
                }
                flight.Body.localPosition = Vector3.Lerp(flight.From,flight.To,t)+Vector3.up*(.26f*Mathf.Sin(t*Mathf.PI));
                if (flight.Spin) flight.Body.localRotation = Quaternion.Euler(0,t*360,0);
                else flight.Body.localRotation = Quaternion.FromToRotation(Vector3.right,flight.To-flight.From);
            }
            AnimateFeatures(chestProps);
            AnimateFeatures(springProps);

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

        /// <summary>A chest or spring that was just used swells and settles back.</summary>
        private static void AnimateFeatures(List<Feature> props)
        {
            foreach (var prop in props)
            {
                float age = Time.time-prop.CueAt;
                if (age < 0 || age > 1) continue;
                float swell = 1+.22f*Pulse(prop.CueAt,.45f);
                prop.Spent.localScale = new Vector3(swell,2-swell,swell);
            }
        }

        private Vector3 NearestFoeDirection(Vector3 from)
        {
            Vector3 best = Vector3.zero;
            float bestDistance = float.MaxValue;
            foreach (var foe in foes)
            {
                if (!foe.Alive || !foe.Engaged) continue;
                float distance = (foe.Body.localPosition-from).sqrMagnitude;
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                best = foe.Body.localPosition-from;
            }
            return best;
        }

        private Vector3 NearestHeroDirection(Vector3 from)
        {
            Vector3 best = Vector3.zero;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < heroes.Length; i++)
            {
                if (!living[i]) continue;
                float distance = (heroes[i].localPosition-from).sqrMagnitude;
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                best = heroes[i].localPosition-from;
            }
            return best;
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
            if (warnMesh) Destroy(warnMesh);
            effects?.Dispose();
            if (warnMaterial && (palette == null || Array.IndexOf(palette, warnMaterial) < 0)) Destroy(warnMaterial);
            foreach (var material in patrolMaterials)
                if (material && (palette == null || Array.IndexOf(palette, material) < 0)) Destroy(material);
            if (palette == null) return;
            foreach (var material in palette) if (material) Destroy(material);
        }
    }
}
