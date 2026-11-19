"use strict";

const hudArt = "../../Assets/Game/Unity/Resources/Hud/";
const heroArt = "../../Assets/Art/Heroes/Portraits/";
const heroTypes = [
  { id: "tank", name: "탱커", role: "앞에서 적을 막습니다.", max: 120 },
  { id: "melee", name: "근거리 딜러", role: "앞에서 가까운 적을 공격합니다.", max: 95 },
  { id: "ranged", name: "원거리 딜러", role: "뒤에서 먼 적을 공격합니다.", max: 88 },
  { id: "healer", name: "힐러", role: "뒤에서 동료를 치유합니다.", max: 92 },
];
const monsters = {
  skeleton: { name: "해골 검사", cost: 1, xp: 4, art: "SkeletonSwordsmanPortrait.png", hint: "앞쪽을 크게 벱니다." },
  ghost: { name: "해골 별점술사", cost: 2, xp: 8, art: "SkeletonAstrologerPortrait.png", hint: "멀리서 쌍묘비를 떨어뜨립니다." },
  zombie: { name: "해골 방패병", cost: 3, xp: 8, art: "SkeletonShieldbearerPortrait.png", hint: "정면의 용사를 밀어냅니다." },
  erase: { name: "배치 지우기", art: "ErasePlacement.png", hint: "한 마리의 비용을 돌려받습니다." },
};
const phases = { placement: "몬스터 배치", formation: "원정대 편성", travel: "탐색 중", encounter: "전투 중", fork: "갈림길 논의", clear: "스테이지 클리어", defeat: "원정대 전멸" };
const recommended = [5, 21];
const fork = [6, 11];
const bossGate = [33, 6];
const routes = [
  { label: "북쪽 길", speaker: "힐러 · 원거리 딜러", points: [[6, 8], [2, 1], [6, 8], [6, 11], [22, 11], bossGate] },
  { label: "동쪽 길", speaker: "탱커 · 근거리 딜러", points: [[22, 11], [18, 4], [22, 6], bossGate] },
];
const levels = [
  { name: "성문으로 가는 길", boss: "무덤 기사", budget: 6, roster: ["skeleton", "ghost"], patrols: [[[6, 11], [22, 9], [21, 23], [22, 9], [6, 11]]] },
  { name: "지하 납골당", boss: "납골당 사제", budget: 8, roster: ["skeleton", "ghost", "zombie"], patrols: [[[8, 5], [26, 6], [26, 18], [8, 18], [8, 5]], [[46, 31], [27, 29], [30, 18], [45, 18], [30, 18], [27, 29], [46, 31]]] },
];

const byId = (id) => document.getElementById(id);
const canvas = byId("map");
const ctx = canvas.getContext("2d");
const sheet = byId("board-sheet");
const recButton = byId("recommended");
const heroCards = heroTypes.map((hero, index) => {
  const card = byId("hero-template").content.firstElementChild.cloneNode(true);
  card.querySelector("img").src = heroArt + "portrait_" + hero.id + ".png";
  card.querySelector("strong").textContent = hero.name;
  card.addEventListener("click", () => bless(index));
  byId("heroes").append(card);
  return card;
});
const art = {};
for (const [id, type] of Object.entries(monsters)) {
  art[id] = new Image();
  art[id].src = hudArt + type.art;
  art[id].onload = () => drawMap();
}
const key = ([x, y]) => x + "," + y;
const equal = (a, b) => key(a) === key(b);
const distance = (a, b) => Math.abs(a[0] - b[0]) + Math.abs(a[1] - b[1]);
const dirs = [[0, -1], [1, 0], [0, 1], [-1, 0]];
let state;
let view;
let timeout;
let lockedSheet = "";
let shownGuide = "";
let pan = [0, 0];
let zoom = 1;
let drag = null;
let hover = null;
let keyboardTile = null;
let noteTimer;

function currentLevel() { return levels[state.stage - 1]; }
function tile(x, y) { return state.map[y]?.[x] || "#"; }
function findPath(start, end) {
  const queue = [start];
  const prior = new Map([[key(start), null]]);
  for (let index = 0; index < queue.length; index++) {
    const point = queue[index];
    if (equal(point, end)) {
      const result = [];
      for (let cursor = point; cursor; cursor = prior.get(key(cursor))) result.push(cursor);
      return result.reverse();
    }
    for (const [dx, dy] of dirs) {
      const next = [point[0] + dx, point[1] + dy];
      if (tile(...next) === "#" || prior.has(key(next))) continue;
      prior.set(key(next), point);
      queue.push(next);
    }
  }
  return [start];
}
function through(points) {
  const result = [points[0]];
  for (let i = 1; i < points.length; i++) result.push(...findPath(points[i - 1], points[i]).slice(1));
  return result;
}
function stageSetup(stage) {
  state.stage = stage;
  state.map = mapRows[stage - 1].trim().split(/\r?\n/);
  const positions = [];
  state.map.forEach((row, y) => Array.from(row).forEach((symbol, x) => {
    if ("ECBS".includes(symbol)) positions.push({ symbol, point: [x, y] });
  }));
  state.entry = positions.find(({ symbol }) => symbol === "E").point;
  state.features = positions.filter(({ symbol }) => "ECS".includes(symbol));
  state.bossPoint = positions.filter(({ symbol }) => symbol === "B").reduce((avg, { point }, _, list) =>
    [avg[0] + point[0] / list.length, avg[1] + point[1] / list.length], [0, 0]).map(Math.round);
  state.patrolPaths = currentLevel().patrols.map(through);
  state.distances = new Map([[key(state.entry), 0]]);
  const queue = [state.entry];
  for (let i = 0; i < queue.length; i++) {
    const point = queue[i];
    for (const [dx, dy] of dirs) {
      const next = [point[0] + dx, point[1] + dy];
      if (tile(...next) === "#" || state.distances.has(key(next))) continue;
      state.distances.set(key(next), state.distances.get(key(point)) + 1);
      queue.push(next);
    }
  }
  Object.assign(state, {
    phase: "placement", tool: null, placed: new Map(), opened: new Set(), position: state.entry,
    speed: 1, choosingHero: false, previewRoute: null, route: [], trail: [], encounter: null, walks: 0,
    bossHp: 100, patrolled: false, fought: false, pathChosen: false,
  });
  pan = [0, 0];
  zoom = 1;
  keyboardTile = state.entry;
  hover = null;
  drawTools();
}
function reset() {
  clearTimeout(timeout);
  clearTimeout(noteTimer);
  byId("map-status").hidden = true;
  state = {
    guide: "map", skipped: false, guideHold: false, faith: 3, xp: 0, level: 1,
    heroes: heroTypes.map(({ max }) => ({ max, hp: max, gear: "" })),
  };
  byId("journal").replaceChildren();
  record("원정대가 입구에서 배치를 기다립니다.");
  stageSetup(1);
  render();
}
function record(message) {
  const row = document.createElement("li");
  row.textContent = message;
  const journal = byId("journal");
  journal.prepend(row);
  if (journal.children.length > 5) journal.lastElementChild.remove();
}
function notice(message) {
  const status = byId("map-status");
  status.hidden = false;
  status.textContent = message;
  clearTimeout(noteTimer);
  noteTimer = setTimeout(() => { status.hidden = true; }, 2400);
}
function guide(next) {
  if (!state.skipped) state.guide = next;
}
const instructions = {
  map: ["용사들이 싸울 길을 준비하세요", "입구에서 출발한 용사들은 길을 찾아 무덤 기사에게 갑니다. 가는 길에 용사들이 이길 만한 전투를 준비해 주세요.", 0, "첫 배치 시작", "startGuide"],
  select: ["해골 검사를 고르세요", "아래 배치판에서 해골 검사를 고르면 지도 위에 추천 타일이 표시됩니다.", 1],
  place: ["빛나는 칸에 놓으세요", "고정 순회로와 떨어진 첫 길목입니다. 금색 타일을 눌러 해골 검사 한 마리를 놓으세요.", 1],
  placed: ["전투가 몰리지 않게 배치하세요", "해골 검사 한 마리에 위협 예산 1을 썼습니다. 배치를 지우면 비용을 돌려받습니다. 더 놓고 싶다면 순회로에서 떨어진 길목을 골라 보세요.", 1],
  free: ["전투가 몰리지 않게 배치하세요", "한곳에 많이 놓으면 용사들이 한꺼번에 싸우게 됩니다. 위협 예산 안에서 길목에 나눠 두거나 바로 출발하세요.", 1],
  formation: ["먼저 용사들의 역할을 확인하세요", "탱커와 근거리 딜러가 앞에 서고, 원거리 딜러와 힐러가 뒤에서 돕습니다. 기본 편성으로 바로 출발할 수 있습니다.", 2],
  observe: ["용사들의 원정을 지켜보세요", "용사들은 길을 탐색하고 적을 만나면 스스로 싸웁니다. 자세히 보고 싶을 때는 일시정지를 눌러 보세요.", 3],
  fight: ["전투가 시작됐습니다", "용사 카드의 체력 막대를 보세요. 적을 쓰러뜨리면 파티 경험치를 얻습니다.", 3],
  growth: ["용사들이 경험을 쌓았습니다", "파란 막대는 파티 경험치입니다. 레벨이 오르면 용사들의 공격력과 최대 체력이 함께 오릅니다.", 4],
  heal: ["축복으로 용사를 도울 수 있습니다", "전투로 파티 경험치를 얻었고 용사들이 피해를 입었습니다. 잠시 멈춘 동안 신앙 1로 다친 용사 한 명을 회복시키거나, 1배속을 눌러 계속하세요.", 4],
  fork: ["이 길에 신탁을 내릴까요?", "길 카드에 마우스를 올리면 지도의 경로가 빛납니다. 신앙을 써서 길을 정하거나, 넘어가서 용사들의 결정을 볼 수 있습니다.", 4],
  chosen: ["용사들이 길을 정했습니다", "신탁과 축복은 같은 신앙을 씁니다. 신앙이 남았다면 나중에 다친 용사를 도울 수 있습니다.", 4],
  chest: ["보물 상자에서 장비를 얻었습니다", "용사들이 탐색 중에 상자를 발견하면 들러서 엽니다. 장착한 장비는 다음 스테이지로 가져갑니다.", 4],
  boss: ["무덤 기사와 싸웁니다", "일반 전투에서 얻은 레벨과 장비를 갖고 도전합니다. 위급하면 시간을 멈추고 축복을 쓸 수 있습니다.", 4],
  clear: ["다음 던전으로 갑니다", "이번에 얻은 파티 레벨과 장착 장비는 다음 스테이지에 이어집니다. 다음에는 어떤 길목에 적을 둘지 직접 정해 보세요.", 5],
  defeat: ["다음에는 전투를 나눠 보세요", "같은 길목에 적을 많이 두면 용사들이 한꺼번에 싸웁니다. 다시 시작해 첫 배치를 바꿔 보세요.", 5],
  next: ["다음 던전을 준비하세요", "용사들이 회복했습니다. 이전 스테이지에서 얻은 레벨과 장비를 확인하고, 새로 열린 해골 방패병을 배치해 볼 수 있습니다.", 5],
  skipped: ["원정 기록", "가이드 안내를 건너뛰었습니다. 배치와 원정 조작은 계속 사용할 수 있습니다.", -1],
};
function renderGuide() {
  const name = state.skipped ? "skipped" : state.guide;
  if (shownGuide === name) return;
  shownGuide = name;
  const [title, body, step, buttonLabel, action] = instructions[name];
  const content = byId("guide-content");
  content.innerHTML = `<h2>${title}</h2><p>${body}</p>${buttonLabel ? `<button class="primary" data-action="${action}" type="button">${buttonLabel} <span aria-hidden="true">→</span></button>` : ""}`;
  const progress = byId("guide-progress");
  progress.hidden = step < 0;
  progress.setAttribute("aria-label", step < 0 ? "가이드 건너뜀" : `가이드 ${step + 1} / 6`);
  progress.innerHTML = Array.from({ length: 6 }, (_, index) =>
    `<span class="${index < step ? "past" : index === step ? "current" : ""}" aria-hidden="true"></span>`).join("");
}
function drawTools() {
  const ids = [...currentLevel().roster, "erase"];
  byId("tools").innerHTML = ids.map((id) => {
    const type = monsters[id];
    const cost = type.cost ? `위협 ${type.cost}` : "비용 반환";
    return `<button type="button" class="monster" data-tool="${id}" title="${type.hint}" aria-pressed="false"><img src="${hudArt + type.art}" alt=""><span>${type.name}<small>${cost}</small></span></button>`;
  }).join("");
}
function usedBudget() {
  return Array.from(state.placed.values()).flat().reduce((sum, id) => sum + monsters[id].cost, 0);
}
function validPlacement(point) {
  return (state.distances.get(key(point)) ?? 0) > 4;
}
function place(point) {
  if (state.phase !== "placement") return;
  if (!state.tool) {
    notice("아래 배치판에서 몬스터를 먼저 고르세요.");
    return;
  }
  const location = key(point);
  const placed = state.placed.get(location) || [];
  if (state.tool === "erase") {
    if (!placed.length) { notice("이 칸에는 지울 몬스터가 없습니다."); return; }
    const removed = placed.pop();
    if (!placed.length) state.placed.delete(location);
    record(`${monsters[removed].name} 배치를 지웠습니다.`);
  } else {
    if (!validPlacement(point)) { notice("입구 주변과 벽에는 놓을 수 없습니다."); return; }
    const type = monsters[state.tool];
    if (usedBudget() + type.cost > currentLevel().budget) { notice("위협 예산이 모자랍니다."); return; }
    placed.push(state.tool);
    state.placed.set(location, placed);
    record(`${type.name} 배치 · 위협 ${type.cost} 사용`);
    if (state.stage === 1 && ["map", "select", "place"].includes(state.guide)) {
      guide(state.tool === "skeleton" ? "placed" : "free");
    }
  }
  render();
}
function setTool(id) {
  if (state.phase !== "placement") return;
  state.tool = id;
  if (state.stage === 1 && ["map", "select", "place"].includes(state.guide)) guide(id === "skeleton" ? "place" : "select");
  if (id === "skeleton" && state.guide === "place" && !state.skipped) resetView();
  render();
}
function renderSheet() {
  const screen = ["formation", "fork", "clear", "defeat"].includes(state.phase) ? state.phase : "";
  const sheetKey = screen === "fork" ? screen + state.faith : screen;
  if (sheetKey === lockedSheet) return;
  lockedSheet = sheetKey;
  sheet.hidden = !screen;
  sheet.classList.toggle("result", ["clear", "defeat"].includes(screen));
  if (!screen) { sheet.replaceChildren(); return; }
  if (screen === "formation") {
    sheet.innerHTML = `<h2>원정대 편성</h2><div class="formation">${heroTypes.map((hero) => `<div><b>${hero.name}</b><small>${hero.role}</small></div>`).join("")}</div><div class="sheet-actions"><button class="secondary" type="button" data-action="back">배치로 돌아가기</button><button type="button" class="primary" data-action="launch">원정 시작 →</button></div>`;
  } else if (screen === "fork") {
    sheet.innerHTML = `<h2>갈림길 논의</h2><p>“북쪽엔 살펴볼 곁방이 있어요.” · “동쪽 통로로 전진하죠.”</p><div class="sheet-actions">${routes.map((route, i) => `<button type="button" class="secondary" data-route="${i}" ${state.faith ? "" : "disabled"}>${route.label} · 신앙 1<small>${route.speaker}</small></button>`).join("")}<button type="button" class="primary" data-action="pass">넘어간다</button></div>`;
  } else if (screen === "clear") {
    const equipment = state.heroes.map((hero) => hero.gear).filter(Boolean).join(" · ");
    sheet.innerHTML = `<div class="result-box"><img src="${hudArt}StageClear.png" alt=""><h2>무덤 기사 격파</h2><p>파티 레벨 ${state.level} · ${equipment}</p><button type="button" class="primary" data-action="next">다음 스테이지 준비 →</button></div>`;
  } else {
    sheet.innerHTML = `<div class="result-box"><img src="${hudArt}GameOver.png" alt=""><h2>원정대가 쓰러졌습니다</h2><button type="button" class="primary" data-action="restart">다시 시작</button></div>`;
  }
  sheet.querySelector(screen === "fork" ? '[data-action="pass"]' : ".primary")?.focus({ preventScroll: true });
}
function render() {
  const preparing = state.phase === "placement" || state.phase === "formation";
  const operating = state.phase === "travel" || state.phase === "encounter";
  byId("phase").textContent = phases[state.phase];
  byId("stage-name").textContent = currentLevel().name;
  byId("stage-number").textContent = `${state.stage}스테이지`;
  byId("placement").hidden = !preparing;
  byId("expedition").hidden = preparing;
  byId("depart").hidden = state.stage === 2;
  byId("depart").disabled = state.phase === "formation";
  byId("skip").hidden = state.skipped || state.stage === 2;
  byId("budget").querySelector("strong").textContent = `${currentLevel().budget - usedBudget()} / ${currentLevel().budget}`;
  for (const button of byId("tools").children) {
    button.disabled = state.phase === "formation";
    button.setAttribute("aria-pressed", button.dataset.tool === state.tool);
    button.classList.toggle("highlight", !state.skipped && state.guide === "select" && button.dataset.tool === "skeleton");
  }
  byId("level").textContent = `레벨 ${state.level}`;
  byId("xp-text").textContent = `${state.xp} / 10`;
  byId("xp").value = state.xp;
  byId("party-xp").classList.toggle("highlight", !state.skipped && ["growth", "heal"].includes(state.guide));
  byId("fatigue").value = state.walks;
  byId("fatigue-text").textContent = state.walks > 156 ? "탈진" : state.walks > 114 ? "지침" : "여유";
  byId("faith-text").textContent = state.faith;
  const pips = byId("faith-pips");
  if (pips.childElementCount === 0) pips.innerHTML = "<i></i>".repeat(5);
  Array.from(pips.children).forEach((pip, i) => pip.classList.toggle("filled", i < state.faith));
  for (const [i, id] of ["pause", "play", "fast"].entries()) {
    byId(id).disabled = !operating;
    byId(id).setAttribute("aria-pressed", operating && state.speed === i);
  }
  byId("pause").classList.toggle("highlight", !state.skipped && state.guide === "observe" && operating);
  byId("bless").disabled = !operating || !state.faith || !state.heroes.some(({ hp, max }) => hp > 0 && hp < max);
  byId("bless").classList.toggle("highlight", !state.skipped && state.guide === "heal" && !state.choosingHero && !byId("bless").disabled);
  byId("bless").setAttribute("aria-pressed", state.choosingHero);
  byId("bless").firstChild.textContent = state.choosingHero ? "축복 취소 " : "축복 ";
  byId("rest").disabled = !operating || !state.faith || !state.walks;
  state.heroes.forEach(({ max, hp, gear }, i) => {
    const card = heroCards[i];
    card.querySelector(".gear").textContent = gear || "장비 없음";
    card.querySelector(".health").textContent = `${hp} / ${max}`;
    card.querySelector(".hp i").style.width = hp / max * 100 + "%";
    card.disabled = !operating || !state.choosingHero || hp <= 0 || hp >= max;
    card.classList.toggle("highlight", !card.disabled);
  });
  renderGuide();
  renderSheet();
  drawMap();
}

function drawMap() {
  if (!state) return;
  const { width, height } = canvas.getBoundingClientRect();
  if (!width || !height) return;
  const ratio = window.devicePixelRatio || 1;
  const pixelWidth = Math.round(width * ratio);
  const pixelHeight = Math.round(height * ratio);
  if (canvas.width !== pixelWidth || canvas.height !== pixelHeight) {
    canvas.width = pixelWidth;
    canvas.height = pixelHeight;
  }
  ctx.setTransform(ratio, 0, 0, ratio, 0, 0);
  const columns = state.map[0].length;
  const rows = state.map.length;
  const size = Math.min((width - 18) / columns, (height - 20) / rows) * zoom;
  view = { size, x: (width - columns * size) / 2 + pan[0], y: (height - rows * size) / 2 + pan[1] };
  const half = size / 2;
  const background = ctx.createRadialGradient(width / 2, height / 2, 30, width / 2, height / 2, width * .65);
  background.addColorStop(0, "#26332a");
  background.addColorStop(1, "#0e1512");
  ctx.fillStyle = background;
  ctx.fillRect(0, 0, width, height);
  for (let y = 0; y < rows; y++) {
    for (let x = 0; x < columns; x++) {
      const symbol = tile(x, y);
      const px = view.x + x * size;
      const py = view.y + y * size;
      if (symbol === "#") {
        if (dirs.some(([dx, dy]) => tile(x + dx, y + dy) !== "#")) {
          ctx.fillStyle = "#465048";
          ctx.fillRect(px + .3, py + .3, size - .6, size - .6);
          ctx.fillStyle = "#526056";
          ctx.fillRect(px + 1, py + 1, size - 2, 1);
        }
        continue;
      }
      const shade = (x * 7 + y * 3) % 3;
      ctx.fillStyle = symbol === "B"
        ? ["#60443b", "#574038", "#66473b"][shade]
        : ["#3d5548", "#354b40", "#40584b"][shade];
      ctx.fillRect(px, py, size, size);
      ctx.strokeStyle = "#12281f5e";
      ctx.lineWidth = .7;
      ctx.strokeRect(px, py, size, size);
    }
  }
  for (const route of state.patrolPaths) drawPath(route, "#b995e4", Math.max(1, size * .16), [4, 5]);
  if (state.trail.length) drawPath(state.trail, "#9bc9b780", Math.max(2, size * .21));
  if (state.previewRoute !== null) {
    const preview = through([fork, ...routes[state.previewRoute].points.slice(0, 2)]).slice(0, 11);
    drawPath(preview, "#f3cd80", Math.max(2, size * .3));
  }
  for (const { symbol, point } of state.features) {
    if (symbol === "E") mapMark(point, "↑", "#b6ead2", "입구");
    if (symbol === "C") mapMark(point, "■", state.opened.has(key(point)) ? "#887c66" : "#f3cd80");
    if (symbol === "S") mapMark(point, "≈", "#9ed9e6", "샘물");
  }
  if (state.bossHp > 0) mapMark(state.bossPoint, "◆", "#f3ab91", currentLevel().boss);
  if (state.phase === "placement") {
    state.patrolPaths.forEach((route) => mapMark(route[0], "•", "#d0b5ed", "순회조"));
  }
  for (const [location, list] of state.placed) {
    const [x, y] = center(location.split(",").map(Number));
    const radius = Math.max(5, size * .47);
    const portrait = art[list.at(-1)];
    ctx.save();
    ctx.beginPath();
    ctx.arc(x, y, radius, 0, Math.PI * 2);
    ctx.fillStyle = "#987466";
    ctx.fill();
    ctx.clip();
    if (portrait.complete && portrait.naturalWidth) ctx.drawImage(portrait, x - radius, y - radius, radius * 2, radius * 2);
    ctx.restore();
    ctx.strokeStyle = "#e69d80";
    ctx.stroke();
    if (list.length > 1) {
      ctx.font = "bold 11px sans-serif";
      ctx.fillStyle = "#fce7b9";
      ctx.fillText("×" + list.length, x + radius, y + 3);
    }
  }
  if (!["placement", "formation"].includes(state.phase)) {
    const [x, y] = center(state.position);
    const radius = Math.max(3, size * .22);
    ctx.save();
    ctx.shadowBlur = 8;
    ctx.shadowColor = state.phase === "encounter" ? "#fe8b60" : "#99ebcf";
    for (const [dx, dy] of [[-1, -1], [1, -1], [-1, 1], [1, 1]]) {
      ctx.beginPath();
      ctx.fillStyle = state.phase === "encounter" ? "#ffb59a" : "#b9f5d7";
      ctx.arc(x + dx * half * .66, y + dy * half * .66, radius, 0, Math.PI * 2);
      ctx.fill();
    }
    ctx.restore();
    if (state.phase === "encounter" && state.encounter.boss) {
      ctx.fillStyle = "#492d28";
      ctx.fillRect(x - 32, y - 24, 64, 5);
      ctx.fillStyle = "#ee8c72";
      ctx.fillRect(x - 32, y - 24, .64 * state.bossHp, 5);
    }
  }
  const cursor = hover || (document.activeElement === canvas ? keyboardTile : null);
  if (cursor && state.phase === "placement" && tile(...cursor) !== "#") {
    const [x, y] = center(cursor);
    ctx.strokeStyle = validPlacement(cursor) ? "#dfcb92" : "#d38c7b";
    ctx.lineWidth = 1.5;
    ctx.strokeRect(x - half, y - half, size, size);
  }
  recButton.hidden = state.skipped || state.guide !== "place" || state.phase !== "placement" || state.tool !== "skeleton";
  if (!recButton.hidden) {
    const [x, y] = center(recommended);
    Object.assign(recButton.style, { left: x + "px", top: y + "px", width: Math.max(18, size) + "px", height: Math.max(18, size) + "px" });
  }
}
function center([x, y]) {
  return [view.x + (x + .5) * view.size, view.y + (y + .5) * view.size];
}
function drawPath(path, color, width, dash = []) {
  ctx.beginPath();
  ctx.strokeStyle = color;
  ctx.lineWidth = width;
  ctx.lineCap = "round";
  ctx.lineJoin = "round";
  ctx.setLineDash(dash);
  path.forEach((point, i) => ctx[i ? "lineTo" : "moveTo"](...center(point)));
  ctx.stroke();
  ctx.setLineDash([]);
}
function mapMark(point, glyph, color, label) {
  const [x, y] = center(point);
  const radius = Math.max(5, view.size * .53);
  ctx.fillStyle = "#18251e";
  ctx.beginPath();
  ctx.arc(x, y, radius, 0, Math.PI * 2);
  ctx.fill();
  ctx.fillStyle = color;
  ctx.textAlign = "center";
  ctx.textBaseline = "middle";
  ctx.font = `bold ${Math.max(9, view.size * .72)}px sans-serif`;
  ctx.fillText(glyph, x, y + .5);
  if (label) {
    ctx.font = "bold 11px 'Malgun Gothic', sans-serif";
    ctx.strokeStyle = "#131d18";
    ctx.lineWidth = 4;
    ctx.strokeText(label, x, y - radius - 7);
    ctx.fillText(label, x, y - radius - 7);
  }
  ctx.textAlign = "start";
}
function resetView() { zoom = 1; pan = [0, 0]; }
function boardPoint(event) {
  const rect = canvas.getBoundingClientRect();
  return [Math.floor((event.clientX - rect.left - view.x) / view.size), Math.floor((event.clientY - rect.top - view.y) / view.size)];
}

function launch() {
  state.phase = "travel";
  state.speed = 1;
  state.pendingHeal = false;
  state.clock = 0;
  state.route = through([state.entry, recommended, fork]).slice(1);
  state.trail = [state.entry];
  guide("observe");
  record("용사들이 던전을 탐색하기 시작했습니다.");
  render();
  schedule(650);
}
function schedule(delay) {
  clearTimeout(timeout);
  if (!state.speed || !["travel", "encounter"].includes(state.phase)) return;
  const base = delay ?? (state.phase === "encounter" ? 650 : 145);
  timeout = setTimeout(() => {
    state.clock += base;
    if (state.clock >= 60000) {
      state.clock -= 60000;
      if (state.faith < 5) { state.faith++; record("신앙이 1 회복됐습니다."); }
    }
    if (state.phase === "encounter") fightTick();
    else travelTick();
    render();
    schedule();
  }, base / state.speed);
}
function setSpeed(speed) {
  if (!["travel", "encounter"].includes(state.phase)) return;
  state.speed = speed;
  if (speed && state.guideHold) {
    state.guideHold = false;
    state.choosingHero = false;
    guide("growth");
  }
  render();
  schedule();
}
function travelTick() {
  if (state.pendingHeal) {
    state.pendingHeal = false;
    if (!state.skipped && state.faith > 0 && state.heroes.some(({ hp, max }) => hp > 0 && hp < max)) {
      state.guideHold = true;
      state.speed = 0;
      guide("heal");
      return;
    }
  }
  if (state.route.length) {
    state.position = state.route.shift();
    state.trail.push(state.position);
    state.walks++;
    if (state.walks === 115) {
      notice("지침 · 파티 공격력이 내려갑니다.");
      record("파티가 오래 걸어 지쳤습니다.");
    }
    if (state.walks > 156 && state.walks % 5 === 0) {
      state.heroes.forEach((hero) => { if (hero.hp) hero.hp = Math.max(1, hero.hp - 2); });
    }
  }
  const symbol = tile(...state.position);
  if (symbol === "C" && !state.opened.has(key(state.position))) {
    state.opened.add(key(state.position));
    const hero = state.heroes.find(({ hp, gear }) => hp > 0 && !gear);
    if (hero) hero.gear = "일반 무기";
    guide("chest");
    record("보물 상자를 열어 일반 무기를 얻었습니다.");
  }
  const nearby = Array.from(state.placed.entries()).filter(([location]) => distance(state.position, location.split(",").map(Number)) <= 1);
  if (nearby.length) {
    for (const [location] of nearby) state.placed.delete(location);
    startFight(nearby.flatMap(([, group]) => group), false);
  } else if (!state.route.length && !state.pathChosen && !state.patrolled) {
    state.patrolled = true;
    startFight(["skeleton", "skeleton"], false, "고정 순회조");
  } else if (!state.route.length && !state.pathChosen) {
    state.phase = "fork";
    state.choosingHero = false;
    guide("fork");
    record("용사들이 갈림길에서 의견을 나눕니다.");
    if (!state.faith) chooseRoute(null);
  } else if (!state.route.length) {
    startFight([], true, "무덤 기사");
  }
}
function startFight(group, boss, title) {
  state.phase = "encounter";
  const name = title || (group.length === 1 ? monsters[group[0]].name : `배치 몬스터 ${group.length}마리`);
  state.encounter = { name, group, boss, turn: 0, turns: boss ? 5 : Math.min(5, group.length + 2) };
  guide(boss ? "boss" : state.fought ? state.guide : "fight");
  record(`${name} 교전 시작`);
}
function fightTick() {
  const fight = state.encounter;
  fight.turn++;
  const difficulty = fight.boss ? Math.max(2, 6 - state.level - state.heroes.filter(({ gear }) => gear).length) : fight.group.reduce((sum, id) => sum + monsters[id].cost, 0);
  const fatiguePenalty = state.walks > 114 ? 2 : 0;
  const baseDamage = difficulty > 3 && !fight.boss ? difficulty * 6 : difficulty * 3 + 3;
  state.heroes.forEach((hero, i) => {
    if (!hero.hp) return;
    const damage = Math.round((baseDamage + fatiguePenalty) * [1, .86, .63, .74][(i + fight.turn - 1) % 4]);
    hero.hp = Math.max(0, hero.hp - damage);
  });
  if (fight.boss) state.bossHp = Math.max(0, 100 - fight.turn / fight.turns * 100);
  if (state.heroes.every(({ hp }) => !hp)) {
    state.phase = "defeat";
    guide("defeat");
    record("원정대가 전멸했습니다.");
    return;
  }
  if (fight.turn < fight.turns) return;
  state.phase = "travel";
  if (fight.boss) {
    addXP(10);
    const survivor = state.heroes.find(({ hp }) => hp > 0);
    survivor.gear = survivor.gear ? survivor.gear + " · 희귀 방어구" : "희귀 방어구";
    state.phase = "clear";
    guide("clear");
    record("무덤 기사 격파 · 희귀 방어구 장착");
    return;
  }
  const xp = fight.group.reduce((sum, id) => sum + monsters[id].xp, 0);
  record(`${fight.name} 격파 · 파티 경험치 +${xp}`);
  addXP(xp);
  if (!state.fought) {
    state.fought = true;
    state.pendingHeal = true;
    guide("growth");
  }
}
function addXP(value) {
  state.xp += value;
  while (state.xp >= 10) {
    state.xp -= 10;
    state.level++;
    state.heroes.forEach((hero) => {
      hero.max += 5;
      if (hero.hp) hero.hp += 5;
    });
    record(`파티가 레벨 ${state.level}에 도달했습니다.`);
  }
}
function chooseRoute(index) {
  if (state.phase !== "fork" || (index !== null && !state.faith)) return;
  const selected = index ?? 1;
  if (index === null) record("설득 주사위 17 대 12 · 파티가 동쪽 길을 골랐습니다.");
  else {
    state.faith--;
    record(`신탁 · ${routes[index].label} 확정 · 신앙 -1`);
  }
  state.pathChosen = true;
  state.previewRoute = null;
  state.phase = "travel";
  state.route = through([state.position, ...routes[selected].points]).slice(1);
  guide("chosen");
  render();
  schedule(420);
}
function bless(index) {
  if (!state.choosingHero || !state.faith || !["travel", "encounter"].includes(state.phase)) return;
  const hero = state.heroes[index];
  const recovered = Math.min(34, hero.max - hero.hp);
  if (!hero.hp || !recovered) return;
  hero.hp += recovered;
  state.faith--;
  state.choosingHero = false;
  record(`축복 · ${heroTypes[index].name} 체력 +${recovered} · 신앙 -1`);
  if (state.guideHold) setSpeed(1);
  else render();
}
function nextStage() {
  state.heroes.forEach((hero) => { hero.hp = hero.max; });
  stageSetup(2);
  guide("next");
  record("2스테이지 준비 · 레벨과 장비를 이어받았습니다.");
  render();
}

byId("tools").addEventListener("click", (event) => {
  const button = event.target.closest("[data-tool]");
  if (button) setTool(button.dataset.tool);
});
byId("guide-content").addEventListener("click", (event) => {
  if (!event.target.closest('[data-action="startGuide"]')) return;
  guide("select");
  render();
  byId("tools").querySelector('[data-tool="skeleton"]').focus();
});
recButton.addEventListener("click", () => {
  place(recommended);
  if (recButton.hidden) byId("depart").focus({ preventScroll: true });
});
byId("depart").addEventListener("click", () => {
  state.phase = "formation";
  guide("formation");
  render();
});
sheet.addEventListener("click", (event) => {
  const button = event.target.closest("button");
  if (!button) return;
  if (button.dataset.route !== undefined) chooseRoute(Number(button.dataset.route));
  switch (button.dataset.action) {
    case "back":
      state.phase = "placement";
      guide(usedBudget() ? "free" : "select");
      render();
      byId("depart").focus();
      break;
    case "launch": launch(); byId("pause").focus({ preventScroll: true }); break;
    case "pass": chooseRoute(null); byId("pause").focus({ preventScroll: true }); break;
    case "next": nextStage(); byId("tools").querySelector("button").focus({ preventScroll: true }); break;
    case "restart": reset(); break;
  }
});
function previewRoute(event) {
  const button = event.target.closest("[data-route]");
  if (!button || state.phase !== "fork") return;
  state.previewRoute = ["pointerover", "focusin"].includes(event.type) ? Number(button.dataset.route) : null;
  drawMap();
}
for (const event of ["pointerover", "pointerout", "focusin", "focusout"]) sheet.addEventListener(event, previewRoute);
byId("skip").addEventListener("click", () => {
  state.skipped = true;
  if (state.guideHold) setSpeed(1);
  else render();
});
byId("restart").addEventListener("click", reset);
byId("whole").addEventListener("click", () => { resetView(); drawMap(); });
["pause", "play", "fast"].forEach((id, i) => byId(id).addEventListener("click", () => setSpeed(i)));
byId("bless").addEventListener("click", () => {
  state.choosingHero = !state.choosingHero;
  render();
  if (state.choosingHero) {
    notice("왼쪽 용사 카드에서 회복할 용사를 고르세요.");
    heroCards.find((card) => !card.disabled)?.focus({ preventScroll: true });
  }
});
byId("rest").addEventListener("click", () => {
  state.walks = Math.max(0, state.walks - 30);
  state.faith--;
  record("피로 회복 · 신앙 -1");
  render();
});
canvas.addEventListener("click", (event) => {
  if (view) place(boardPoint(event));
});
canvas.addEventListener("contextmenu", (event) => event.preventDefault());
canvas.addEventListener("pointerdown", (event) => {
  if (![1, 2].includes(event.button)) return;
  event.preventDefault();
  drag = [event.clientX - pan[0], event.clientY - pan[1]];
  canvas.setPointerCapture(event.pointerId);
});
canvas.addEventListener("pointermove", (event) => {
  if (drag) pan = [event.clientX - drag[0], event.clientY - drag[1]];
  hover = view && boardPoint(event);
  drawMap();
});
for (const event of ["pointerup", "pointercancel", "lostpointercapture"]) canvas.addEventListener(event, () => { drag = null; });
canvas.addEventListener("pointerleave", () => { hover = null; drawMap(); });
canvas.addEventListener("wheel", (event) => {
  event.preventDefault();
  zoom = Math.max(1, Math.min(2.8, zoom * (event.deltaY > 0 ? .88 : 1.14)));
  if (zoom === 1) pan = [0, 0];
  drawMap();
}, { passive: false });
canvas.addEventListener("focus", drawMap);
canvas.addEventListener("blur", drawMap);
canvas.addEventListener("keydown", (event) => {
  if (state.phase !== "placement") return;
  const i = ["ArrowUp", "ArrowRight", "ArrowDown", "ArrowLeft"].indexOf(event.key);
  if (i >= 0) {
    event.preventDefault();
    const [dx, dy] = dirs[i];
    keyboardTile = [
      Math.max(0, Math.min(state.map[0].length - 1, keyboardTile[0] + dx)),
      Math.max(0, Math.min(state.map.length - 1, keyboardTile[1] + dy)),
    ];
    hover = null;
    drawMap();
  } else if (event.key === "Enter") {
    event.preventDefault();
    place(keyboardTile);
  }
});
document.addEventListener("keydown", (event) => {
  if (event.repeat || event.altKey || event.ctrlKey || event.metaKey) return;
  if (state.phase === "fork" && ["1", "2", "3"].includes(event.key)) {
    chooseRoute(event.key === "3" ? null : Number(event.key) - 1);
  } else if (["travel", "encounter"].includes(state.phase)) {
    if (event.key === " " && !event.target.closest("button")) { event.preventDefault(); setSpeed(state.speed ? 0 : 1); }
    if (["1", "2"].includes(event.key)) setSpeed(Number(event.key));
  }
});
new ResizeObserver(drawMap).observe(canvas);

// 던전 지도는 DungeonLayouts.cs의 1·2스테이지를 사용합니다.
const mapRows = [
`
############################################
##C.........################################
##..........#####################BBBBBBBBB##
##..........#####################BBBBBBBBB##
##..........######C.........#####BBBBBBBBB##
##..........######..........#####BBBBBBBBB##
######.###########...............BBBBBBBBB##
######.###########..........#####BBBBBBBBB##
##..........######..........#####BBBBBBBBB##
##..........######..........#####BBBBBBBBB##
##..........######..........#####BBBBBBBBB##
##..........................################
##..........######..........################
##..........######..........################
##..........##########.#####################
##..........##########.#####################
#####.################.#####################
#####.################.#####################
#####.################.#####################
#####.###########..........#################
##........#######..........######........###
##........#######..........######........###
##........#######..........######........###
##.........................######........###
##........#######........................###
##........#######..........######........###
##...E....#######..........######.......C###
##........#######..........#################
############################################
############################################
`,
`
#####################################################
#####################################################
##C.........########.............#######BBBBBBBBBBB##
##..........########.S...........#######BBBBBBBBBBB##
##..........########.............#######BBBBBBBBBBB##
##......................................BBBBBBBBBBB##
##..........########.............#######BBBBBBBBBBB##
##..........########.............#######BBBBBBBBBBB##
##..........########.............#######BBBBBBBBBBB##
#######.############.............#######BBBBBBBBBBB##
#######.##################.##################.#######
#######.##################.##################.#######
#######.##################.##################.#######
#######.##########.................C#########.#######
###..........#####..................#####.........###
###..........#####..................#####.........###
###..........#####..................#####.........###
###..........#####..................#####.........###
###...............................................###
###..........#####..................#####.........###
###..........#####..................#####.........###
###..........#####..................#####.........###
#######.##########..................#####.........###
#######.##########..................#################
#######.###################.#########################
#######.###################.#########################
#######.###################.#########################
##............#######............####################
##............#######............#########.........##
##............#######............#########.........##
##.................................................##
##..E.........#######............#########.........##
##............#######............#########.........##
##............############################........C##
#####################################################
#####################################################
`,
];
reset();
