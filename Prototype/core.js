// 던전 타이쿤 웹 프로토타입 · 규칙 코어
// gamerules.md의 흐름을 그대로 옮긴 순수 로직. 화면·타이머는 index.html이 담당한다.
// TUNE 표시가 붙은 수치는 문서가 "첫 플레이에서 조정할 값"으로 남겨둔 임시값이다.
(function (root) {
  'use strict';

  // ---------- 데이터 (TUNE) ----------
  const HERO_BASE = {
    tank:   { name: '가론',   role: '탱커',     icon: '🛡️', hp: 120, atk: 9,  heal: 0,  mods: { str: 3, dex: 0, wis: 0 } },
    healer: { name: '세레나', role: '힐러',     icon: '✨', hp: 65,  atk: 5,  heal: 20, mods: { str: 0, dex: 1, wis: 3 } },
    melee:  { name: '카이',   role: '근거리 딜러', icon: '⚔️', hp: 80,  atk: 17, heal: 0,  mods: { str: 2, dex: 1, wis: 0 } },
    ranged: { name: '리아',   role: '원거리 딜러', icon: '🏹', hp: 62,  atk: 14, heal: 0,  mods: { str: 0, dex: 3, wis: 1 } },
  };
  const ROLE_ORDER = ['tank', 'healer', 'melee', 'ranged'];
  const STAT_NAME = { str: '힘', dex: '민첩', wis: '지혜' };

  const MONSTERS = {
    goblin: { name: '고블린',   icon: '👺', cost: 1, xp: 30,  units: [{ hp: 32, atk: 7 }] },
    wolves: { name: '늑대 무리', icon: '🐺', cost: 2, xp: 65,  units: [{ hp: 26, atk: 8 }, { hp: 26, atk: 8 }] },
    orc:    { name: '오크 전사', icon: '👹', cost: 3, xp: 105, units: [{ hp: 90, atk: 13 }] },
    knight: { name: '해골 기사', icon: '💀', cost: 4, xp: 150, units: [{ hp: 140, atk: 17 }] },
  };

  const STAGES = [
    { name: '1층 · 고블린 동굴', budget: 10, length: 8, monsters: ['goblin', 'wolves', 'orc'],
      boss: { name: '트롤 족장', icon: '🧌', hp: 420, atk: 18, aoeEvery: 3, xp: 200 } },
    { name: '2층 · 마왕의 성채', budget: 14, length: 8, monsters: ['wolves', 'orc', 'knight'],
      boss: { name: '마왕 벨자크', icon: '😈', hp: 3000, atk: 45, aoeEvery: 3, xp: 0 } },
  ];

  const START_FAITH = 4;
  const FAITH_PER_ENCOUNTER = 1;
  const ADVERSITY_PER_STAGE = 2;
  const ADVERSITY_REWARD = { xp: 70, faith: 3 };

  const HELPS = {
    heal:   { name: '치유의 손길', icon: '💚', cost: 2, target: 'alive', desc: '용사 한 명의 HP를 50 회복' },
    bless:  { name: '전쟁의 축복', icon: '🔥', cost: 3, target: 'none',  desc: '이번 조우 동안 파티 공격력 +30%', combatOnly: true },
    revive: { name: '부활',       icon: '🕊️', cost: 5, target: 'dead',  desc: '쓰러진 용사를 HP 40%로 되살림' },
  };

  const ADVERSITIES = [
    { id: 'rockfall', name: '낙석',         ctx: ['path', 'battle', 'boss'], role: 'tank',   stat: 'str', dc: 14, dmg: 24,
      line: '방패를 들어 동료들의 머리 위를 막겠습니다!' },
    { id: 'trap',     name: '숨겨진 함정',   ctx: ['path'],                  role: 'ranged', stat: 'dex', dc: 13, dmg: 20,
      line: '저 압력판, 제가 먼저 쏴서 터뜨리죠.' },
    { id: 'miasma',   name: '저주받은 안개', ctx: ['path', 'boss'],          role: 'healer', stat: 'wis', dc: 13, dmg: 18,
      line: '정화의 빛으로 안개를 걷어내겠습니다.' },
    { id: 'ambush',   name: '기습 증원',     ctx: ['battle'],                role: 'melee',  stat: 'str', dc: 14, dmg: 22,
      line: '뒤는 제가 막습니다. 먼저 베고 보죠!' },
    { id: 'rage',     name: '보스의 격노',   ctx: ['boss'],                  role: 'tank',   stat: 'str', dc: 15, dmg: 32,
      line: '모두 내 뒤로! 이 일격은 내가 받는다!' },
  ];

  // ---------- 유틸 ----------
  function mulberry32(seed) {
    let a = seed >>> 0;
    return function () {
      a = (a + 0x6D2B79F5) >>> 0;
      let t = a;
      t = Math.imul(t ^ (t >>> 15), t | 1);
      t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
      return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
    };
  }

  // Core/D20Check.cs와 같은 판정식: d20 + 보정 ≥ 난이도
  function d20Succeeds(roll, modifier, difficulty) {
    if (roll < 1 || roll > 20) throw new RangeError('A d20 roll must be between 1 and 20.');
    return roll + modifier >= difficulty;
  }

  function xpToNext(level) { return 60 + 50 * level; } // TUNE

  // ---------- 상태 ----------
  function createGame(seed) {
    const rng = mulberry32(seed == null ? (Date.now() & 0xffffffff) : seed);
    const heroes = ROLE_ORDER.map(key => {
      const b = HERO_BASE[key];
      return { id: key, key, name: b.name, role: b.role, icon: b.icon, level: 1, xp: 0,
        maxHp: b.hp, hp: b.hp, atk: b.atk, heal: b.heal, mods: { ...b.mods }, alive: true };
    });
    const g = {
      rng, heroes, faith: START_FAITH, stageIndex: 0, phase: 'prep',
      placements: [], adversityLeft: ADVERSITY_PER_STAGE, pos: -1,
      encounter: null, encounterSeq: 0, log: [], stats: [], blessed: false,
    };
    startStagePrep(g, 0);
    return g;
  }

  function log(g, text, kind) { g.log.push({ text, kind: kind || 'info', stage: g.stageIndex }); }
  function stage(g) { return STAGES[g.stageIndex]; }
  function stat(g) { return g.stats[g.stageIndex]; }
  function aliveHeroes(g) { return g.heroes.filter(h => h.alive); }
  function hero(g, id) { return g.heroes.find(h => h.id === id); }

  function startStagePrep(g, idx) {
    g.stageIndex = idx;
    g.phase = 'prep';
    g.placements = new Array(STAGES[idx].length).fill(null);
    g.adversityLeft = ADVERSITY_PER_STAGE;
    g.pos = -1;
    g.encounter = null;
    g.stats[idx] = { xp: 0, damageTaken: 0, healed: 0, faithSpent: 0, faithGained: 0, kills: 0, adversity: [] };
  }

  function budgetUsed(g) {
    return g.placements.reduce((s, m) => s + (m ? MONSTERS[m].cost : 0), 0);
  }

  function place(g, cell, monsterKey) {
    if (g.phase !== 'prep') return { ok: false, reason: '준비 단계가 아닙니다.' };
    if (monsterKey && !stage(g).monsters.includes(monsterKey)) return { ok: false, reason: '이 층에 배치할 수 없는 몬스터입니다.' };
    const prev = g.placements[cell];
    g.placements[cell] = monsterKey || null;
    if (budgetUsed(g) > stage(g).budget) { g.placements[cell] = prev; return { ok: false, reason: '위협 예산을 넘습니다.' }; }
    return { ok: true };
  }

  function depart(g) {
    if (g.phase !== 'prep') return;
    // 쓰러진 용사도 스테이지 입구에서 복귀한다. 입구에서 모두 정비한다(TUNE: 전원 완전 회복).
    g.heroes.forEach(h => { if (!h.alive) log(g, `${h.icon} ${h.name}이(가) 입구에서 복귀했다.`, 'good'); h.alive = true; h.hp = h.maxHp; });
    g.phase = 'run';
    g.pos = -1;
    log(g, `━━ ${stage(g).name} 원정 시작 ━━`, 'stage');
  }

  // 한 칸 전진. 도착한 칸의 조우를 반환한다: 'path' | 'battle' | 'boss'
  function step(g) {
    if (g.phase !== 'run' || g.encounter) return null;
    g.pos += 1;
    if (g.pos >= stage(g).length) { startEncounter(g, 'boss'); return 'boss'; }
    const m = g.placements[g.pos];
    if (m) { startEncounter(g, 'battle', m); return 'battle'; }
    return 'path';
  }

  function startEncounter(g, kind, monsterKey) {
    let enemies;
    let name;
    if (kind === 'boss') {
      const b = stage(g).boss;
      name = b.name;
      enemies = [{ id: 'boss', name: b.name, icon: b.icon, hp: b.hp, maxHp: b.hp, atk: b.atk, aoeEvery: b.aoeEvery, alive: true }];
    } else {
      const m = MONSTERS[monsterKey];
      name = m.name;
      enemies = m.units.map((u, i) => ({ id: `${monsterKey}${i}`, name: m.units.length > 1 ? `${m.name.replace(' 무리', '')} ${i + 1}` : m.name,
        icon: m.icon, hp: u.hp, maxHp: u.hp, atk: u.atk, alive: true }));
    }
    g.encounterSeq += 1;
    g.encounter = { id: g.encounterSeq, kind, monsterKey, name, enemies, round: 0, taunt: false, over: false };
    g.blessed = false;
    log(g, kind === 'boss' ? `⚠ 보스 ${name} 등장!` : `${MONSTERS[monsterKey].icon} ${name}과(와) 조우했다.`, kind === 'boss' ? 'boss' : 'warn');
  }

  function context(g) {
    if (g.phase !== 'run') return null;
    if (!g.encounter) return 'path';
    return g.encounter.kind;
  }

  // ---------- 용사 행동 ----------
  function aliveEnemies(g) { return g.encounter ? g.encounter.enemies.filter(e => e.alive) : []; }

  // 역할별 기본 행동 (AI 응답이 늦거나 무효일 때)
  function defaultDecision(g, h) {
    const foes = aliveEnemies(g);
    if (h.key === 'healer') {
      const t = lowestAlly(g);
      if (t && t.hp / t.maxHp < 0.75) return { action: 'heal', target: t.id };
    }
    return { action: 'attack', target: foes[0] && foes[0].id };
  }

  function lowestAlly(g) {
    return aliveHeroes(g).slice().sort((a, b) => a.hp / a.maxHp - b.hp / b.maxHp)[0];
  }

  // 현재 상태에서 결정이 여전히 가능한지 검사. 불가하면 이유 문자열.
  function invalidReason(g, h, d) {
    if (!h.alive) return '이미 쓰러졌다';
    if (!d) return '결정 없음';
    if (d.action === 'attack') {
      const e = g.encounter.enemies.find(x => x.id === d.target);
      if (!e || !e.alive) return '대상이 이미 쓰러졌다';
    } else if (d.action === 'heal') {
      if (h.key !== 'healer') return '치료할 수 없다';
      const t = hero(g, d.target);
      if (!t || !t.alive) return '치료 대상이 쓰러졌다';
      if (t.hp >= t.maxHp * 0.95) return '치료 대상이 이미 회복됐다';
    } else if (d.action === 'taunt') {
      if (h.key !== 'tank') return '도발할 수 없다';
    } else return '알 수 없는 행동';
    return null;
  }

  // 결정을 적용한다. 한 라운드에 한 용사는 한 번만 행동한다.
  function applyHeroAction(g, h, d, source) {
    const enc = g.encounter;
    if (!enc || enc.over || !aliveEnemies(g).length) return { applied: false, reason: '조우 종료' };
    if (h.actedRound === enc.id * 1000 + enc.round) return { applied: false, reason: '이미 행동함' };
    let note = '';
    const bad = invalidReason(g, h, d);
    if (bad) {
      if (!h.alive) return { applied: false, reason: bad };
      note = `(${bad} → 기본 행동)`;
      d = defaultDecision(g, h);
      source = 'default';
    }
    h.actedRound = enc.id * 1000 + enc.round;
    const r = g.rng;
    if (d.action === 'attack') {
      const e = enc.enemies.find(x => x.id === d.target);
      const dmg = Math.max(1, Math.round(h.atk * (g.blessed ? 1.3 : 1) * (0.9 + r() * 0.2)));
      e.hp = Math.max(0, e.hp - dmg);
      log(g, `${h.icon} ${h.name} → ${e.name} ${dmg} 피해 ${note}`, 'hit');
      if (e.hp <= 0) { e.alive = false; log(g, `${e.icon} ${e.name} 처치!`, 'good'); }
    } else if (d.action === 'heal') {
      const t = hero(g, d.target);
      const amt = Math.min(t.maxHp - t.hp, Math.round(h.heal * (0.9 + r() * 0.2)));
      t.hp += amt;
      stat(g).healed += amt;
      log(g, `${h.icon} ${h.name} → ${t.name} HP ${amt} 회복 ${note}`, 'heal');
    } else if (d.action === 'taunt') {
      enc.taunt = true;
      log(g, `${h.icon} ${h.name}이(가) 도발! 이번 라운드 공격을 모두 받아낸다.`, 'hit');
    }
    return { applied: true, action: d, source, note };
  }

  // 라운드 종료: 적 행동. 결과 'continue' | 'win' | 'wipe'
  function enemyPhase(g) {
    const enc = g.encounter;
    if (!enc || enc.over) return null;
    const r = g.rng;
    for (const e of aliveEnemies(g)) {
      const targets = aliveHeroes(g);
      if (!targets.length) break;
      if (e.aoeEvery && enc.round % e.aoeEvery === e.aoeEvery - 1) {
        log(g, `${e.icon} ${e.name}의 광역 공격!`, 'boss');
        targets.forEach(t => damageHero(g, t, Math.round(e.atk * 0.55 * (0.9 + r() * 0.2)), e));
        continue;
      }
      const tank = targets.find(t => t.key === 'tank');
      const t = tank && (enc.taunt || r() < 0.6) ? tank : targets[Math.floor(r() * targets.length)];
      let dmg = e.atk * (0.85 + r() * 0.3);
      if (t.key === 'tank') dmg *= enc.taunt ? 0.5 : 0.75;
      damageHero(g, t, Math.round(dmg), e);
    }
    enc.taunt = false;
    enc.round += 1;
    return checkEnd(g);
  }

  function damageHero(g, h, dmg, src) {
    const real = Math.min(h.hp, dmg);
    h.hp -= real;
    stat(g).damageTaken += real;
    log(g, `${src.icon} ${src.name} → ${h.name} ${dmg} 피해`, 'dmg');
    if (h.hp <= 0) { h.alive = false; log(g, `💀 ${h.icon} ${h.name}이(가) 쓰러졌다!`, 'bad'); }
  }

  function gainXp(g, amount, reason) {
    stat(g).xp += amount;
    for (const h of aliveHeroes(g)) {
      h.xp += amount;
      while (h.xp >= xpToNext(h.level)) {
        h.xp -= xpToNext(h.level);
        h.level += 1;
        const hpUp = Math.round(h.maxHp * 0.14);
        h.maxHp += hpUp; h.hp += hpUp;
        h.atk = Math.round(h.atk * 1.15);
        h.heal = Math.round(h.heal * 1.15);
        if (h.level % 2 === 1) Object.keys(h.mods).forEach(k => { if (h.mods[k] > 0) h.mods[k] += 1; });
        log(g, `⬆ ${h.icon} ${h.name} 레벨 ${h.level}!`, 'level');
      }
    }
    log(g, `파티 경험치 +${amount} (${reason})`, 'xp');
  }

  function gainFaith(g, n, reason) {
    g.faith += n; stat(g).faithGained += n;
    log(g, `🙏 신앙 +${n} (${reason})`, 'faith');
  }

  function checkEnd(g) {
    const enc = g.encounter;
    if (!enc || enc.over) return null;
    if (!aliveHeroes(g).length) {
      enc.over = true;
      g.phase = 'defeat';
      log(g, '☠ 파티 전멸. 원정은 패배로 끝났다.', 'bad');
      return 'wipe';
    }
    if (!aliveEnemies(g).length) {
      enc.over = true;
      if (enc.kind === 'boss') {
        stat(g).kills += 1;
        if (g.stageIndex === STAGES.length - 1) {
          g.phase = 'victory';
          log(g, `👑 ${enc.name} 처치! 살아남은 용사 ${aliveHeroes(g).length}명. 원정 승리!`, 'good');
        } else {
          gainXp(g, stage(g).boss.xp, '보스 처치');
          g.phase = 'stageClear';
          log(g, `🏆 ${enc.name} 처치! 다음 층으로.`, 'good');
        }
      } else {
        stat(g).kills += 1;
        gainXp(g, MONSTERS[enc.monsterKey].xp, `${enc.name} 처치`);
        gainFaith(g, FAITH_PER_ENCOUNTER, '조우 승리');
        // 짧은 정비: 힐러가 살아 있으면 전원 소량 회복
        const healer = aliveHeroes(g).find(h => h.key === 'healer');
        if (healer) aliveHeroes(g).forEach(h => {
          const amt = Math.min(h.maxHp - h.hp, Math.round(healer.heal * 0.6));
          h.hp += amt; stat(g).healed += amt;
        });
        g.encounter = null;
      }
      g.blessed = false;
      return 'win';
    }
    return 'continue';
  }

  function nextStage(g) {
    if (g.phase !== 'stageClear') return;
    startStagePrep(g, g.stageIndex + 1);
  }

  // ---------- 사회자 개입 ----------
  function canHelp(g, key, targetId) {
    const help = HELPS[key];
    if (g.phase !== 'run') return '원정 중에만 쓸 수 있다';
    if (g.faith < help.cost) return '신앙이 부족하다';
    if (help.combatOnly && !g.encounter) return '전투 중에만 쓸 수 있다';
    if (key === 'bless' && g.blessed) return '이미 축복 중이다';
    if (help.target !== 'none') {
      const h = hero(g, targetId);
      if (!h) return '대상을 고르세요';
      if (help.target === 'alive' && !h.alive) return '쓰러진 용사다';
      if (help.target === 'dead' && h.alive) return '살아 있는 용사다';
      if (key === 'heal' && h.hp >= h.maxHp) return '이미 최대 HP다';
    }
    return null;
  }

  function applyHelp(g, key, targetId) {
    const bad = canHelp(g, key, targetId);
    if (bad) return { ok: false, reason: bad };
    const help = HELPS[key];
    g.faith -= help.cost; stat(g).faithSpent += help.cost;
    const h = hero(g, targetId);
    if (key === 'heal') {
      const amt = Math.min(h.maxHp - h.hp, 50);
      h.hp += amt; stat(g).healed += amt;
      log(g, `${help.icon} 사회자의 도움: ${h.name} HP ${amt} 회복 (신앙 -${help.cost})`, 'god');
    } else if (key === 'bless') {
      g.blessed = true;
      log(g, `${help.icon} 사회자의 도움: 전쟁의 축복! 파티 공격력 +30% (신앙 -${help.cost})`, 'god');
    } else if (key === 'revive') {
      h.alive = true; h.hp = Math.round(h.maxHp * 0.4);
      log(g, `${help.icon} 사회자의 도움: ${h.name} 부활! (신앙 -${help.cost})`, 'god');
    }
    return { ok: true };
  }

  function adversityOptions(g) {
    const c = context(g);
    if (!c) return [];
    return ADVERSITIES.filter(a => a.ctx.includes(c));
  }

  // 대표 용사: 해당 역할이 쓰러졌으면 그 능력치가 가장 높은 생존자
  function representative(g, adv) {
    const own = hero(g, adv.role);
    if (own.alive) return own;
    return aliveHeroes(g).slice().sort((a, b) => b.mods[adv.stat] - a.mods[adv.stat])[0];
  }

  function canAdversity(g) {
    if (g.phase !== 'run') return '원정 중에만 선언할 수 있다';
    if (g.adversityLeft <= 0) return '이 층의 역경을 모두 사용했다';
    if (g.encounter && g.encounter.over) return '조우가 끝났다';
    return null;
  }

  // 선언 즉시 횟수가 줄어든다. roll은 1~20 (테스트용으로 주입 가능)
  function declareAdversity(g, advId, roll) {
    const bad = canAdversity(g);
    if (bad) return { ok: false, reason: bad };
    const adv = ADVERSITIES.find(a => a.id === advId);
    if (!adv || !adv.ctx.includes(context(g))) return { ok: false, reason: '지금은 선언할 수 없는 역경' };
    g.adversityLeft -= 1;
    const rep = representative(g, adv);
    if (roll == null) roll = 1 + Math.floor(g.rng() * 20);
    const mod = rep.mods[adv.stat];
    const success = d20Succeeds(roll, mod, adv.dc);
    const line = rep.key === adv.role ? adv.line : '제가 대신 맡겠습니다!';
    const res = { ok: true, adv, rep, roll, mod, dc: adv.dc, success, line, statName: STAT_NAME[adv.stat] };
    log(g, `🎲 역경 [${adv.name}] ${rep.name}: "${line}" — d20 ${roll} + ${STAT_NAME[adv.stat]} ${mod} = ${roll + mod} vs 난이도 ${adv.dc} → ${success ? '성공' : '실패'}`, success ? 'good' : 'bad');
    stat(g).adversity.push({ name: adv.name, success });
    if (success) {
      gainXp(g, ADVERSITY_REWARD.xp, '역경 극복');
      gainFaith(g, ADVERSITY_REWARD.faith, '역경 극복');
    } else {
      damageHero(g, rep, adv.dmg, { icon: '💥', name: adv.name });
      if (g.encounter) res.end = checkEnd(g);
      else if (!aliveHeroes(g).length) { g.phase = 'defeat'; log(g, '☠ 파티 전멸. 원정은 패배로 끝났다.', 'bad'); }
    }
    return res;
  }

  const api = {
    HERO_BASE, ROLE_ORDER, MONSTERS, STAGES, HELPS, ADVERSITIES, STAT_NAME, ADVERSITY_PER_STAGE,
    createGame, d20Succeeds, xpToNext, budgetUsed, place, depart, step, context, aliveHeroes, aliveEnemies,
    defaultDecision, lowestAlly, invalidReason, applyHeroAction, enemyPhase, checkEnd, nextStage,
    canHelp, applyHelp, adversityOptions, representative, canAdversity, declareAdversity, stage, hero,
  };
  root.DT = api;
  if (typeof module !== 'undefined') module.exports = api;
})(typeof window !== 'undefined' ? window : globalThis);
