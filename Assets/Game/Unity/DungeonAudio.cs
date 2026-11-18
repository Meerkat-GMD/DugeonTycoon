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
        PrayerAnswer, OracleSign, NecromancerMarch, VictoryReprise
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
        private readonly SoundKey[] music = { SoundKey.Planning, SoundKey.Exploration, SoundKey.BossBattle,
            SoundKey.NecromancerMarch, SoundKey.CryptAmbience };

        public DungeonAudio(GameObject owner)
        {
            foreach (AudioGroup group in System.Enum.GetValues(typeof(AudioGroup)))
                mix[group] = Mathf.Clamp(PlayerPrefs.GetInt("Volume." + group, 100), 0, 200);
            Register(SoundKey.Planning, "whispers_in_the_fog", true);
            Register(SoundKey.Exploration, "rpg_ambience_-_dungeon", true);
            Register(SoundKey.BossBattle, "ghosts_n_heroes_2025_loop", true);
            Register(SoundKey.LootDice, "dice-throw-1", false);
            Register(SoundKey.CryptAmbience, "dark_cavern_ambient_002", true);
            Register(SoundKey.ShieldImpact, "impactMetal_heavy_000", false, .56f);
            Register(SoundKey.Hit, "impactGeneric_light_000", false, .16f);
            Register(SoundKey.Shot, "impactWood_light_000", false, .16f);
            Register(SoundKey.Heal, "impactGlass_light_000", false, .16f);
            Register(SoundKey.Taunt, "impactBell_heavy_000", false, .28f);
            Register(SoundKey.ShieldWall, "impactMetal_light_000", false, .32f);
            Register(SoundKey.PowerStrike, "impactPunch_heavy_000", false, .38f);
            Register(SoundKey.Whirlwind, "impactTin_medium_000", false, .30f);
            Register(SoundKey.PiercingShot, "impactPlank_medium_000", false, .30f);
            Register(SoundKey.RapidFire, "impactPlate_light_000", false, .30f);
            Register(SoundKey.MassHeal, "impactGlass_medium_000", false, .30f);
            Register(SoundKey.Revive, "powerup-get", false, .42f);
            foreach (var key in new[] { SoundKey.WarnSlam, SoundKey.WarnSoulBolt, SoundKey.WarnBeam,
                SoundKey.WarnMeteor, SoundKey.WarnCleave, SoundKey.WarnShove, SoundKey.WarnTombstones, SoundKey.WarnBomb })
                Register(key, key.ToString(), false, .26f);
            Register(SoundKey.ChestLatch, "ChestLatch", false, .33f);
            Register(SoundKey.ChestReward, "powerup-get", false, .30f);
            Register(SoundKey.LevelRise, "LevelRise", false, .32f);
            Register(SoundKey.SkillAwaken, "SkillAwaken", false, .36f);
            Register(SoundKey.PrayerAnswer, "PrayerAnswer", false, .42f);
            Register(SoundKey.OracleSign, "OracleSign", false, .38f);
            Register(SoundKey.VictoryReprise, "VictoryReprise", false, .48f);
            Register(SoundKey.NecromancerMarch, "NecromancerMarch", true);
            Register(SoundKey.Reward, "powerup-get", false, .30f);
            Register(SoundKey.Spring, "impactGlass_light_000", false, .24f);
            Register(SoundKey.Step, "footstep_concrete_000", false, .10f);
            Register(SoundKey.Click, "ui-click", false, .24f);
            Register(SoundKey.Down, "life-lost", false, .26f);
            Register(SoundKey.Clear, "level-complete", false, .36f);
            Register(SoundKey.Defeat, "game-over", false, .36f);

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

        public void PlayEffect(SoundKey key)
        {
            float now = Time.unscaledTime;
            if (nextEffect.TryGetValue(key, out float next) && now < next) return;
            nextEffect[key] = now + (key == SoundKey.Step ? .24f : .08f);
            sources[key].Play();
        }

        public void PlayEvents(IReadOnlyList<CombatEvent> events)
        {
            foreach (var e in events)
            {
                SoundKey? key = e.Kind switch
                {
                    CombatEventKind.Attack when e.PushedHero => SoundKey.ShieldImpact,
                    CombatEventKind.Attack when e.FromHero && e.Source == (int)HeroRole.RangedDamage => SoundKey.Shot,
                    CombatEventKind.Attack => SoundKey.Hit,
                    CombatEventKind.Heal => SoundKey.Heal,
                    CombatEventKind.Chest => SoundKey.ChestLatch,
                    CombatEventKind.Drop => SoundKey.Reward,
                    CombatEventKind.Spring => SoundKey.Spring,
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
                if (e.Kind == CombatEventKind.Chest)
                    sources[SoundKey.ChestReward].PlayDelayed(sources[SoundKey.ChestLatch].clip.length);
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
                    source.pitch = Mathf.Lerp(.85f, 1.8f, Mathf.InverseLerp(cast.StartTick, cast.LockTick, tick));
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
                float volume = ambient ? .085f : MusicVolume;
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
