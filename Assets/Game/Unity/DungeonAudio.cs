using System.Collections.Generic;
using DungeonTycoon.Core;
using UnityEngine;

namespace DungeonTycoon.Unity
{
    internal enum AudioGroup { Bgm, Effects, Ambience }

    internal enum SoundKey
    {
        Planning, Exploration, BossBattle, CryptAmbience, LootDice, ShieldImpact, Hit, Shot, Heal,
        Taunt, ShieldWall, PowerStrike, Whirlwind, PiercingShot, RapidFire, MassHeal, Revive,
        WarnSlam, WarnSoulBolt, WarnBeam, WarnMeteor, WarnCleave, WarnShove, WarnTombstones, WarnBomb,
        Reward, ChestLatch, ChestReward, Spring, Step, Click, Down, Clear, Defeat, LevelRise, SkillAwaken,
        PrayerAnswer, OracleSign, NecromancerMarch, VictoryReprise,
        SwordSwing, HeavySwordSwing, Coin, PlacePiece, ChestLid, Title,
        StartGame, PageTurn, StepSurface, SlamImpact, TombstonesImpact, Prologue
    }

    internal sealed class DungeonAudio
    {
        private const float MusicVolume = .33f;
        private const float FadeSeconds = 1.5f;
        private readonly Dictionary<SoundKey, AudioSource> sources = new();
        // Store the unscaled level (including the current fade) independently of the player's mix.
        private readonly Dictionary<SoundKey, float> levels = new();
        private readonly Dictionary<AudioGroup, int> mix = new();
        private readonly Dictionary<SoundKey, float> nextEffect = new();
        private readonly AudioClip[] stepClips;
        private readonly AudioClip[] grassSteps;
        private readonly AudioClip[] stoneSteps;
        private int nextStep;
        private readonly SoundKey[] music = { SoundKey.Title, SoundKey.Prologue, SoundKey.Planning, SoundKey.Exploration, SoundKey.BossBattle,
            SoundKey.NecromancerMarch, SoundKey.CryptAmbience };

        public DungeonAudio(GameObject owner)
        {
            foreach (AudioGroup group in System.Enum.GetValues(typeof(AudioGroup)))
                mix[group] = Mathf.Clamp(PlayerPrefs.GetInt("Volume." + group, 100), 0, 200);
            Register(SoundKey.Title, "cave_in_game", true);
            Register(SoundKey.Prologue, "PrologueExploration", true);
            Register(SoundKey.Planning, "whispers_in_the_fog", true);
            Register(SoundKey.Exploration, "great_labyrinth_bpm160", true);
            Register(SoundKey.BossBattle, "ghosts_n_heroes_2025_loop", true);
            Register(SoundKey.LootDice, "dice-throw-1", false);
            Register(SoundKey.CryptAmbience, "dark_cavern_ambient_002", true);
            Register(SoundKey.ShieldImpact, "Kenney/impactMetal_heavy_003", false, .42f);
            Register(SoundKey.SlamImpact, "Kenney/impactMining_002", false, .39f);
            Register(SoundKey.TombstonesImpact, "Kenney/impactMining_004", false, .33f);
            Register(SoundKey.Hit, "Combat/BodyImpact", false, .23f);
            Register(SoundKey.SwordSwing, "Combat/SwordSwing", false, .20f);
            Register(SoundKey.HeavySwordSwing, "Combat/HeavySwordSwing", false, .25f);
            Register(SoundKey.Shot, "impactWood_light_000", false, .16f);
            Register(SoundKey.Heal, "impactGlass_light_000", false, .16f);
            Register(SoundKey.Taunt, "impactBell_heavy_000", false, .28f);
            Register(SoundKey.ShieldWall, "impactMetal_light_000", false, .32f);
            Register(SoundKey.PowerStrike, "Combat/PowerStrike", false, .33f);
            Register(SoundKey.Whirlwind, "Combat/Whirlwind", false, .31f);
            Register(SoundKey.PiercingShot, "impactPlank_medium_000", false, .30f);
            Register(SoundKey.RapidFire, "impactPlate_light_000", false, .30f);
            Register(SoundKey.MassHeal, "impactGlass_medium_000", false, .30f);
            Register(SoundKey.Revive, "powerup-get", false, .42f);
            foreach (var key in new[] { SoundKey.WarnSlam, SoundKey.WarnSoulBolt, SoundKey.WarnBeam,
                SoundKey.WarnMeteor, SoundKey.WarnCleave, SoundKey.WarnShove, SoundKey.WarnTombstones, SoundKey.WarnBomb })
                Register(key, key.ToString(), false, .26f);
            Register(SoundKey.ChestLatch, "Kenney/metalLatch", false, .35f);
            Register(SoundKey.ChestLid, "Kenney/doorOpen_1", false, .30f);
            Register(SoundKey.ChestReward, "Kenney/handleCoins", false, .34f);
            Register(SoundKey.LevelRise, "LevelRise", false, .32f);
            Register(SoundKey.SkillAwaken, "SkillAwaken", false, .36f);
            Register(SoundKey.PrayerAnswer, "PrayerAnswer", false, .42f);
            Register(SoundKey.OracleSign, "OracleSign", false, .38f);
            Register(SoundKey.VictoryReprise, "VictoryReprise", false, .48f);
            Register(SoundKey.NecromancerMarch, "NecromancerMarch", true);
            Register(SoundKey.Reward, "Kenney/metalPot1", false, .23f);
            Register(SoundKey.Coin, "Kenney/handleCoins2", false, .34f);
            Register(SoundKey.Spring, "impactGlass_light_000", false, .24f);
            Register(SoundKey.Step, "Kenney/footstep01", false, .14f);
            stepClips = new[] { sources[SoundKey.Step].clip,
                Resources.Load<AudioClip>("Audio/Kenney/footstep03"),
                Resources.Load<AudioClip>("Audio/Kenney/footstep06") };
            Register(SoundKey.StepSurface, "Kenney/footstep_grass_001", false, .055f);
            grassSteps = LoadSurfaceSteps("grass");
            stoneSteps = LoadSurfaceSteps("concrete");
            Register(SoundKey.Click, "ui-click", false, .18f);
            Register(SoundKey.StartGame, "Kenney/jingles_PIZZI08", false, .29f);
            Register(SoundKey.PageTurn, "Kenney/bookFlip3", false, .27f);
            Register(SoundKey.PlacePiece, "Kenney/bookPlace3", false, .32f);
            Register(SoundKey.Down, "Combat/HeroFall", false, .39f);
            Register(SoundKey.Clear, "Kenney/jingles_PIZZI02", false, .36f);
            Register(SoundKey.Defeat, "Kenney/jingles_PIZZI07", false, .36f);

            AudioClip[] LoadSurfaceSteps(string surface) => new[]
            {
                Resources.Load<AudioClip>($"Audio/Kenney/footstep_{surface}_001"),
                Resources.Load<AudioClip>($"Audio/Kenney/footstep_{surface}_002"),
                Resources.Load<AudioClip>($"Audio/Kenney/footstep_{surface}_003")
            };

            void Register(SoundKey key, string file, bool loop, float volume = .65f)
            {
                var source = owner.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.loop = loop;
                source.clip = Resources.Load<AudioClip>("Audio/" + file);
                sources.Add(key, source);
                levels[key] = loop ? 0f : volume;
                ApplyVolume(key);
            }
        }

        private AudioGroup GroupOf(SoundKey key) => key switch
        {
            SoundKey.CryptAmbience => AudioGroup.Ambience,
            SoundKey.PrayerAnswer or SoundKey.OracleSign or SoundKey.VictoryReprise => AudioGroup.Bgm,
            _ => sources[key].loop ? AudioGroup.Bgm : AudioGroup.Effects
        };

        private void ApplyVolume(SoundKey key) =>
            sources[key].volume = Mathf.Clamp01(levels[key] * mix[GroupOf(key)] / 100f);

        public int GetVolume(AudioGroup group) => mix[group];

        public void SetVolume(AudioGroup group, int percent)
        {
            mix[group] = Mathf.Clamp(percent, 0, 200);
            PlayerPrefs.SetInt("Volume." + group, mix[group]);
            foreach (var key in sources.Keys)
                if (GroupOf(key) == group) ApplyVolume(key);
        }

        private bool CanPlayEffect(SoundKey key, float interval)
        {
            float now = Time.unscaledTime;
            if (nextEffect.TryGetValue(key, out float next) && now < next) return false;
            nextEffect[key] = now + interval;
            return true;
        }

        public void PlayFootstep(int stage)
        {
            if (!CanPlayEffect(SoundKey.Step, .24f)) return;
            var step = sources[SoundKey.Step];
            var surface = sources[SoundKey.StepSurface];
            step.clip = stepClips[nextStep];
            surface.clip = (stage == 1 ? grassSteps : stoneSteps)[nextStep];
            nextStep = (nextStep + 1) % stepClips.Length;
            step.Play();
            surface.Play();
        }

        public void PlayEffect(SoundKey key)
        {
            if (!CanPlayEffect(key, .08f)) return;
            var source = sources[key];
            source.Play();
            if (key == SoundKey.ChestLatch)
            {
                var lid = sources[SoundKey.ChestLid];
                lid.PlayDelayed(source.clip.length);
                sources[SoundKey.ChestReward].PlayDelayed(source.clip.length + lid.clip.length);
            }
        }

        public void PlayEvents(IReadOnlyList<CombatEvent> events)
        {
            bool meleeSkillPlayed = false;
            foreach (var e in events)
            {
                if (e.Kind == CombatEventKind.Attack && e.FromHero)
                {
                    switch ((HeroRole)e.Source)
                    {
                        case HeroRole.Tank: PlayEffect(SoundKey.SwordSwing); break;
                        case HeroRole.MeleeDamage when !meleeSkillPlayed: PlayEffect(SoundKey.HeavySwordSwing); break;
                        case HeroRole.RangedDamage: PlayEffect(SoundKey.Shot); break;
                    }
                }

                SoundKey? key = e.Kind switch
                {
                    CombatEventKind.Attack when e.PushedHero => SoundKey.ShieldImpact,
                    CombatEventKind.Attack => SoundKey.Hit,
                    CombatEventKind.Heal => SoundKey.Heal,
                    CombatEventKind.Chest => SoundKey.ChestLatch,
                    CombatEventKind.Drop => SoundKey.Reward,
                    CombatEventKind.Spring => SoundKey.Spring,
                    CombatEventKind.PatternFired => (MonsterPattern)e.Amount switch
                    {
                        MonsterPattern.Slam => SoundKey.SlamImpact,
                        MonsterPattern.Tombstones => SoundKey.TombstonesImpact,
                        _ => null
                    },
                    CombatEventKind.Pattern => (MonsterPattern)e.Amount switch
                    {
                        MonsterPattern.Slam => SoundKey.WarnSlam,
                        MonsterPattern.SoulBolt => SoundKey.WarnSoulBolt,
                        MonsterPattern.Meteor => SoundKey.WarnMeteor,
                        MonsterPattern.Cleave => SoundKey.WarnCleave,
                        MonsterPattern.Shove => SoundKey.WarnShove,
                        MonsterPattern.Tombstones => SoundKey.WarnTombstones,
                        MonsterPattern.Bomb => SoundKey.WarnBomb,
                        // The tracking beam follows the live aim and stops at lock.
                        _ => null
                    },
                    CombatEventKind.Skill => (HeroSkill)e.Amount switch
                    {
                        HeroSkill.Taunt => SoundKey.Taunt,
                        HeroSkill.ShieldWall => SoundKey.ShieldWall,
                        HeroSkill.PowerStrike => SoundKey.PowerStrike,
                        HeroSkill.Whirlwind => SoundKey.Whirlwind,
                        HeroSkill.PiercingShot => SoundKey.PiercingShot,
                        HeroSkill.RapidFire => SoundKey.RapidFire,
                        HeroSkill.MassHeal => SoundKey.MassHeal,
                        HeroSkill.Revive => SoundKey.Revive,
                        _ => null
                    },
                    // Per-target Taunt events share the cue already played at skill activation.
                    _ => null
                };
                if (key.HasValue) PlayEffect(key.Value);
                if (key is SoundKey.PowerStrike or SoundKey.Whirlwind) meleeSkillPlayed = true;
            }
        }

        public void UpdateBeam(IReadOnlyList<PatternCast> casts, float tick, bool running)
        {
            var source = sources[SoundKey.WarnBeam];
            if (running)
            {
                for (int i = 0; i < casts.Count; i++)
                {
                    var cast = casts[i];
                    if (cast.Pattern != MonsterPattern.TrackingBeam || cast.Phase != CastPhase.Aiming || !cast.Caster.Alive) continue;
                    source.pitch = Mathf.Lerp(.95f, 1.05f, Mathf.InverseLerp(cast.StartTick, cast.LockTick, tick));
                    if (!source.isPlaying) source.Play();
                    return;
                }
            }
            if (source.isPlaying) source.Stop();
        }

        public void UpdateMusic(SoundKey? target, int stage, bool praying)
        {
            foreach (var key in music)
            {
                var source = sources[key];
                bool ambient = key == SoundKey.CryptAmbience;
                float volume = key switch
                {
                    SoundKey.CryptAmbience => .085f,
                    SoundKey.Exploration => .28f,
                    _ => MusicVolume
                };
                bool active = ambient ? stage == 2 && target == SoundKey.Exploration : key == target;
                if (active && !source.isPlaying) source.Play();
                bool duck = !ambient && (praying || sources[SoundKey.PrayerAnswer].isPlaying || sources[SoundKey.OracleSign].isPlaying);
                float targetLevel = active ? volume * (duck ? .4f : 1f) : 0f;
                levels[key] = Mathf.MoveTowards(levels[key], targetLevel, volume * Time.unscaledDeltaTime / FadeSeconds);
                ApplyVolume(key);
                if (!active && levels[key] == 0f && source.isPlaying) source.Stop();
            }
        }
    }
}
