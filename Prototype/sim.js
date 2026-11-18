// 밸런스 점검용 헤드리스 시뮬레이터. 브라우저 콘솔에서 DTSim.report() 실행.
// gamerules.md "첫 플레이 검증 기준"의 빈 던전 / 성장의 가치 시나리오를 반복 실행한다.
(function (root) {
  'use strict';
  const DT = root.DT;

  // 사회자 정책: 위급한 용사 치유, 쓰러지면 부활, 보스전 첫 라운드 축복.
  // finalAllIn=true면 마왕전에서 신앙을 모두 쏟는다.
  function godAct(g, finalAllIn) {
    const enc = g.encounter;
    const isFinal = enc && enc.kind === 'boss' && g.stageIndex === DT.STAGES.length - 1;
    const reserve = isFinal || !finalAllIn ? 0 : 6;
    if (enc && enc.kind === 'boss' && !g.blessed && g.faith - 3 >= reserve) DT.applyHelp(g, 'bless');
    for (const h of g.heroes) {
      if (!h.alive && g.faith - 5 >= reserve && enc && enc.kind === 'boss') DT.applyHelp(g, 'revive', h.id);
    }
    for (const h of DT.aliveHeroes(g)) {
      while (h.hp / h.maxHp < 0.4 && g.faith - 2 >= reserve && !DT.canHelp(g, 'heal', h.id)) DT.applyHelp(g, 'heal', h.id);
    }
  }

  function runCombat(g, finalAllIn) {
    let guard = 0;
    while (g.encounter && !g.encounter.over && guard++ < 500) {
      godAct(g, finalAllIn);
      for (const h of DT.aliveHeroes(g)) DT.applyHeroAction(g, h, DT.defaultDecision(g, h), 'default');
      if (DT.checkEnd(g) !== 'continue') break;
      DT.enemyPhase(g);
    }
  }

  // placement: [stage0 cells[], stage1 cells[]], advRoll: 강제 주사위 눈(20=전부 성공)
  function runOnce(seed, placement, advRoll) {
    const g = DT.createGame(seed);
    for (let s = 0; s < DT.STAGES.length; s++) {
      (placement[s] || []).forEach((m, i) => { if (m) DT.place(g, i, m); });
      DT.depart(g);
      let advUsed = 0;
      while (g.phase === 'run') {
        const ctx = DT.step(g);
        if (ctx === 'path' && advUsed < DT.ADVERSITY_PER_STAGE) {
          const opt = DT.adversityOptions(g)[0];
          DT.declareAdversity(g, opt.id, advRoll); advUsed++;
          if (g.phase !== 'run') break;
        }
        if (ctx === 'battle' || ctx === 'boss') {
          if (ctx === 'boss') while (advUsed < DT.ADVERSITY_PER_STAGE) {
            DT.declareAdversity(g, 'rockfall', advRoll); advUsed++;
          }
          runCombat(g, true);
        }
      }
      if (g.phase === 'stageClear') { DT.nextStage(g); continue; }
      break;
    }
    const boss = g.encounter && g.encounter.enemies[0];
    return { win: g.phase === 'victory', phase: g.phase, stage: g.stageIndex,
      levels: g.heroes.map(h => h.level).join('/'), faithLeft: g.faith,
      bossHpLeft: boss ? boss.hp : null, stats: g.stats };
  }

  const PLANS = {
    empty: [[], []],
    goblins: [Array(8).fill('goblin').slice(0, 8).map((m, i) => i < 8 ? m : null), ['wolves', 'wolves', 'wolves', 'wolves', 'wolves', 'wolves', 'wolves']],
    balanced: [['goblin', 'wolves', null, 'orc', 'goblin', null, 'wolves', 'goblin'], ['wolves', 'orc', null, 'knight', 'wolves', null, 'orc', null]],
    heavy: [['orc', null, 'orc', null, 'orc', null, 'goblin', null], ['knight', null, 'knight', null, 'orc', null, 'wolves', null]],
  };

  function report(n) {
    n = n || 200;
    const out = {};
    for (const [name, plan] of Object.entries(PLANS)) {
      for (const roll of [20, null]) {
        let wins = 0, dkHp = 0, dkCount = 0, wipeStage0 = 0; const lv = {};
        for (let i = 0; i < n; i++) {
          const r = runOnce(1000 + i, plan, roll);
          if (r.win) wins++;
          if (r.phase === 'defeat' && r.stage === 0) wipeStage0++;
          if (r.stage === 1 && r.bossHpLeft != null && !r.win) { dkHp += r.bossHpLeft; dkCount++; }
          lv[r.levels] = (lv[r.levels] || 0) + 1;
        }
        out[`${name} / 역경 ${roll === 20 ? '전부 성공' : '무작위'}`] = {
          승률: (wins / n * 100).toFixed(0) + '%', '1층 전멸': wipeStage0,
          '패배 시 마왕 잔여 HP': dkCount ? Math.round(dkHp / dkCount) : '-',
          '대표 레벨': Object.entries(lv).sort((a, b) => b[1] - a[1])[0][0],
        };
      }
    }
    return out;
  }

  root.DTSim = { runOnce, report, PLANS };
})(typeof window !== 'undefined' ? window : globalThis);
