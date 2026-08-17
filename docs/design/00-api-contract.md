# poe.ninja 응답 계약 — 실측 정정

| | |
|---|---|
| 문서 상태 | 실측 확정 |
| 측정일 | 2026-08-15 |
| 측정 리그 | `Allflame` (현재 챌린지 리그) |
| 목적 | `docs/REQUIREMENTS.md` §6의 필드명이 **실제 raw API와 다르다**는 사실을 기록하고, 설계가 의존할 정확한 계약을 확정한다 |

---

## 0. 왜 이 문서가 필요한가

`REQUIREMENTS.md` §6은 필드를 `name` · `value.amount` · `valueAlt` · `volume` · `topPair` · `changePercent`로 명세한다.
**이 이름들은 raw poe.ninja 응답에 존재하지 않는다.** 요구사항 수집 당시 `poe` MCP 서버를 통해 조회했고, 그 서버가 정규화해 내보낸 이름을 기록한 것이다.

앱은 MCP를 거치지 않고 poe.ninja를 직접 호출하므로, **§6의 필드명을 그대로 코딩하면 전부 null이 된다.**

> **§6의 값·수치·결론은 모두 유효하다.** 틀린 것은 필드 이름뿐이다. 아래 §3에서 FR-04-5 공식이 raw API에서도 정확히 성립함을 실측으로 확인했다.

---

## 1. 엔드포인트

### 1.1 리그 목록 — FR-02-4 (기존 미명세, 이번에 확정)

```
GET https://poe.ninja/poe1/api/economy/leagues
```

응답 (실측 전문):

```json
[
  { "id": "Allflame",          "name": "Allflame" },
  { "id": "Hardcore Allflame", "name": "Hardcore Allflame" },
  { "id": "Standard",          "name": "Standard" },
  { "id": "Hardcore",          "name": "Hardcore" }
]
```

| 관찰 | 설계 영향 |
|---|---|
| **현재 챌린지 리그를 지정하는 플래그가 없다.** 판별 근거는 배열 순서(첫 원소)뿐이다 | FR-02-3은 **순서 규약에 의존하는 취약한 계약** 위에 서 있다. 방어 필요 → §4 |
| 상시 리그(`Standard`/`Hardcore`)와 챌린지 리그(`Allflame`/`Hardcore Allflame`)가 한 배열에 섞여 있고, 구분 필드가 없다 | 하드코어/소프트코어 구분도 이름 접두사 추측 외 수단이 없다 |
| 구 엔드포인트 `https://poe.ninja/api/data/getindexstate` 는 **HTTP 404** | 사용 불가. 대안 없음 |

### 1.2 카테고리 목록 — **존재하지 않음** (실측)

exchange 카테고리 목록을 주는 엔드포인트는 없다. 리그 목록에는 대응물(`/economy/leagues`)이 있지만 카테고리에는 없다.

| 시도한 경로 | 결과 |
|---|---|
| `GET /poe1/api/economy/exchange/categories` | **HTTP 404** |
| `GET /poe1/api/economy/categories` | **HTTP 404** |

따라서 18종 열거는 **코드에 상수로 두고 리그 교체 시 수동 확인**하는 수밖에 없다.
새 리그가 새 카테고리를 추가하면 앱은 그것을 알 수 없다 (설계 §10 Q9).

### 1.3 카테고리별 시세 — FR-02-1/2

```
GET https://poe.ninja/poe1/api/economy/exchange/current/overview?league={league}&type={category}
```

`{category}` 는 §6의 exchange 18종 (`Currency` · `Scarab` · `Essence` · …).

---

## 2. 응답 구조 (실측)

> **⚠ 정정 (2026-08-16, 구현 후 실측).** 초판은 최상위 키를 `core`와 `lines` **둘**로 그렸다. **실제로는 셋이며, 누락된 `items`가 이름의 유일한 출처다.**
> `core.items`는 이름 표가 아니라 **환율 기준**이다 — 18개 카테고리 전부에서 정확히 `[chaos, divine]` 둘뿐이고, 959개 line 중 **2개(0.2%)**만 조인된다.
> 최상위 `items[]`는 line당 하나씩 **959/959, 이름 공백 0건**이다. 아래 §2.0을 정본으로 한다.

```jsonc
{
  "core": {
    "primary":   "chaos",     // 이 응답 전체의 기준 통화
    "secondary": "divine",    // 보조 환산 통화
    "items": [                // 배열이다. 맵이 아니다
      {
        "id":        "divine",
        "name":      "Divine Orb",
        "image":     "/gen/image/WzI1LDE0...png",   // poe.ninja 상대 경로
        "category":  "Currency",
        "detailsId": "divine-orb"
      }
    ],
    "rates": {                // 평평한 id → 수치 맵
      "divine": 0.005139      // = 1 / 194.6 (카오스당 디바인 개수)
    }
  },
  "lines": [                  // 시세 본체
    {
      "id":                 "vivid-lifeforce",
      "primaryValue":       0.06401,    // core.primary 단위 = 카오스
      "volumePrimaryValue": 2097323,
      "maxVolumeCurrency":  "divine",   // 가장 많이 거래된 상대 통화
      "maxVolumeRate":      3040,       // 그 통화 1개당 이 아이템 개수
      "sparkline": {
        "totalChange": 30.46,                                    // %
        "data": [-3.45, 5.88, 5.61, 18.18, 35.97, 31.76, 30.46]  // 7점, 누적 변동률(%)
      }
    }
  ]
}
```

### 2.0 최상위 구조 — **정본** 【구현 후 실측 2026-08-16】

문서 루트에는 키가 **셋** 있다.

```jsonc
{
  "core":  { "primary": "chaos", "secondary": "divine",
             "items": [ /* 정확히 2개: chaos, divine — 환율 기준이다 */ ],
             "rates": { "divine": 0.005139 } },
  "lines": [ /* 시세 본체. 959개 (Allflame 18카테고리 합) */ ],
  "items": [ /* ★ 이름의 출처. line당 하나씩 959개 */
    { "id": "hinekoras-lock", "name": "Hinekora's Lock",
      "category": "Currency", "detailsId": "hinekoras-lock",
      "image": "/gen/image/...png" }
  ]
}
```

| 배열 | 원소 수 | 조인율 | 정체 |
|---|---|---|---|
| `core.items` | **항상 2** (`chaos`, `divine`) | 959 중 **2 (0.2%)** | **환율 기준.** `core.rates`와 짝이다 |
| 최상위 `items` | **line과 1:1** | 959 중 **959 (100%)** | **이름 표.** 공백 이름 0건 |

18개 카테고리 전부, 두 리그(`Allflame`·`Standard`)에서 확인했다.

**line에는 이름 필드가 없다** — 959개 line 전수에서 키는 정확히 여섯이다: `id` · `primaryValue` · `volumePrimaryValue` · `maxVolumeCurrency` · `maxVolumeRate` · `sparkline{totalChange,data}`. 초판의 line 표는 옳았다. **틀린 것은 한 층 위였다.**

> **왜 놓쳤는가.** 초판은 `Currency` 응답에서 `divine` 항목이 `core.items` 안에 있는 것을 확인하고 그것을 이름 출처로 기록했다. **그 배열에 그 둘밖에 없다는 사실을 확인하지 않았고, 문서 루트를 열거하지도 않았다.** 예상한 것을 찾아 확인하고 실제로 무엇이 있는지 세지 않은 것이며, ClearType 측정이 실패한 방식과 같다(`00-shell-measurements.md` §11.1).

**서버 측 지역화는 없다.** `language=ko` · `lang=ko` · `language=ko-KR` · `locale=ko` 전부 200을 반환하지만 이름이 영문과 **한 글자도 다르지 않다**. FR-07-3의 한글 사전은 앱이 직접 채워야 한다 — **그 사전의 출처는 poe.ninja가 아니라 GGG의 trade static API다. §6이 정본이다.**

---

### 2.1 필드 대응표 — §6 → 실제

| `REQUIREMENTS.md` §6 표기 | 실제 raw 필드 | 비고 |
|---|---|---|
| `id` | `lines[].id` | **동일.** FR-01-5·FR-07-2의 키로 그대로 사용 |
| `name` | **최상위 `items[].name`** | **`lines`에 없다.** **최상위** `items`를 `id`로 조인한다(§2.0). ~~`core.items`~~는 `[chaos, divine]` 둘뿐이라 조인율 0.2%다 |
| `value.amount` | `lines[].primaryValue` | |
| `value.currency` | `core.primary` | **항목별이 아니라 응답 전역에 하나.** 실측값 `"chaos"` |
| `valueAlt` | 대응 필드 **없음** | `core.rates` + `core.secondary`로 유도 가능하나 **쓸 필요가 없다** (FR-04-5가 금지) |
| `volume` | `lines[].volumePrimaryValue` | FR-04-1에 따라 미표시 |
| `topPair.currency` | `lines[].maxVolumeCurrency` | **FR-04-3 `자동` 모드의 판단 근거** |
| `topPair.rate` | `lines[].maxVolumeRate` | 검산용. 계산 입력으로는 쓰지 않는다 |
| `changePercent` | `lines[].sparkline.totalChange` | FR-04의 변동률 |
| (§6에 없음) | **최상위** `items[].image` | 아이콘. 상대 경로이므로 `https://poe.ninja` 접두 필요. **DivinationCard 전 항목에는 없다**(959개 중 576개만 보유). **읽지만 매핑하지 않는다** — FR-04-6은 아이콘을 쓰되 출처를 GGG static으로 잡았다(A7·§6.6). 두 출처의 결측 집합이 같은 것은 우연이 아니라 같은 사실이다 |
| (§6에 없음) | `core.items[].category` | **`core.items`의** 것. 질의 `type`과 일치하므로 A6의 자기기술 검증에 쓴다 |
| (§6에 없음) | **최상위** `items[].category` | **표시용 분류**이며 질의 `type`과 **다르다**(`Fragments`·`Cards`·`Essences`·`Catalysts`·`Ancestor`·`Delve`). **A6 검증에 쓰면 안 된다** — 상시 불일치 경고가 뜬다 |
| (§6에 없음) | `lines[].sparkline.data` | §3.2 참조 |

---

## 3. FR-04-5 검증 — raw API에서도 공식이 성립하는가

실측 (`league=Allflame`, 2026-08-15):

| 값 | 출처 |
|---|---|
| `vivid-lifeforce.primaryValue` = **0.06401** | `Currency` 응답 |
| `divine.primaryValue` = **194.6** | 같은 응답의 `id: "divine"` 라인 |

```
0.06401 ÷ 194.6      = 0.00032894 divine / 개
1 ÷ 0.00032894       = 3040.0 개 / divine
실제 maxVolumeRate   = 3040          ✅ 일치
```

**결론: FR-04-5의 `value.amount ÷ Divine Orb 시세` 직접 계산은 raw API 필드로 `primaryValue ÷ divine.primaryValue`이며, 결과가 API 자체의 `maxVolumeRate`와 정확히 일치한다.**

`REQUIREMENTS.md` §6이 기록한 수치(0.0644 / 194.9 / 3026)는 더 이른 시점의 스냅샷이며, 공식의 타당성은 시점과 무관하게 재확인됐다.

`valueAlt` 금지 근거도 그대로 유효하다 — raw API에는 애초에 그 필드가 없으므로, **금지 대상 경로가 구조적으로 존재하지 않는다.** FR-04-5는 자동 충족된다.

### 3.1 divine 시세를 얻는 두 경로

| 경로 | 값 | 판단 |
|---|---|---|
| `Currency` 응답의 `lines[]` 중 `id == "divine"` → `primaryValue` | 194.6 (카오스) | **채택.** 다른 아이템과 동일한 취급, 단위가 명시적 |
| `core.rates.divine` | 0.005139 (= 1/194.6) | 보조. 역수라 정밀도 손실 위험이 `valueAlt`와 같은 성격 |

`core.rates`는 소수 6자리로 보이나 반올림 폭이 명시돼 있지 않다. **`lines[]`의 `primaryValue`를 1차 출처로 삼는다.**

### 3.2 `sparkline.data` — §6 "시계열이 없다"에 대한 정정

`REQUIREMENTS.md` §6 주의 1은 *"응답에 시계열 이력이 없다. 현재 스냅샷만 제공된다"* 고 적었다. 정확히는:

- **절대 가격의 시계열은 없다** — 이 부분은 맞다.
- 그러나 **7점짜리 누적 변동률(%) 배열이 있다** (`divine`: `[-0.25, -2.01, -7.08, -8.73, -10.11, -9.47, -9.18]`, 마지막 원소가 `totalChange`와 동일).

| 함의 | 판정 |
|---|---|
| 1차 범위(D2 차트 없음)에는 영향 없음 | 범위 변경 없음 |
| §11 "이력 차트 — poe.ninja 과거 데이터 엔드포인트 존재 여부 조사"에 대한 **부분적 답** | 스파크라인은 저장 없이 그릴 수 있다. 단 상대값이라 절대 가격 차트는 여전히 불가 |
| 기준 시점·간격(일 단위? 시간 단위?)이 문서화돼 있지 않다 | 도입 시 추가 실측 필요 |

---

## 4. 설계에 미치는 영향 (S2/S3 필수 입력)

| # | 사실 | 설계 요구 |
|---|---|---|
| A1 | 아이템 이름이 `lines`에 없다 | 매핑 계층은 **최상위 `items` 조인을 반드시 수행**한다(§2.0). ~~`core.items`~~를 조인하면 959개 중 2개만 맞는다 — **구현 후 실측으로 정정** |
| A2 | 최상위 `items`가 배열이다 | 응답당 1회 `id → item` 사전을 구축한 뒤 조인. 선형 탐색 금지 |
| A3 | 기준 통화가 응답 전역(`core.primary`)에 하나뿐이다 | 도메인 모델은 항목별 통화를 갖지 않는다. 카테고리 스냅샷 헤더에 둔다. `core.primary != "chaos"` 인 경우를 **검증하고 거부**할 것 (전제 붕괴 감지) |
| A4 | 리그 목록에 현재 챌린지 리그 플래그가 없다 | 첫 원소 채택 + 방어: 배열이 비었거나, 첫 원소가 `Standard`/`Hardcore` 인 경우를 이상으로 간주하고 사용자에게 리그 명시 선택을 요구 |
| A5 | `valueAlt`가 raw에 없다 | FR-04-5 위반 경로가 구조적으로 부재. 설계는 이 사실을 명시하고 `core.rates` 역수 사용도 함께 금지 |
| A6 | `core.items[].category` 존재 | FR-01-1 카탈로그가 카테고리별 18회 호출을 하더라도, 각 응답이 자기 카테고리를 자기 기술(self-describing)한다 |
| A7 | `image`가 상대 경로 | **닫힘 (2026-08-17).** 아이콘을 쓴다(FR-04-6). **다만 poe.ninja의 `image`는 쓰지 않는다** — 이름과 같은 이유로 출처는 GGG trade static이며(§6), 런타임에 CDN을 치지 않고 빌드 타임에 받아 동봉한다(A8과 같은 논거). 매핑·결측·컬러키는 §6.6 |
| A8 | **이 API는 한글 이름을 어떤 매개변수로도 주지 않는다**(§2.0 말미) | 아이템 이름 사전은 **다른 출처(GGG trade static, §6)에서 빌드 타임에 생성**해 사전 파일로 동봉한다. 런타임에 이 API 밖으로 나가는 호출을 추가하지 않는다 — NFR-02와 어긋나고 앱을 제3의 가용성에 묶는다 |

---

## 5. `REQUIREMENTS.md` 정정 권고

§6 「응답 항목별 필드」 표와 「주의」 1항은 사실과 다르다. 다음 중 하나가 필요하다.

1. **권고** — §6을 이 문서의 §2.1 대응표로 교체하고, 주의 1항을 §3.2 내용으로 정정한다.
2. 또는 §6을 "MCP 경유 관측 기록"으로 성격을 재정의하고, 이 문서를 정식 데이터 계약으로 승격한다.

어느 쪽이든 **설계·구현은 이 문서를 따른다.** `REQUIREMENTS.md` §6의 필드명을 그대로 구현하면 동작하지 않는다.

---

## 6. 한글 아이템 이름의 출처 — GGG trade static 【실측 2026-08-16, 리그 `Allflame`】

§2.0이 확정했듯 poe.ninja는 한글 이름을 주지 않는다. 이 절은 **어디서 받아 무엇으로 잇는지**를 계약으로 고정한다. 사전 자체는 빌드 타임에 생성되어 `ko.json`으로 동봉되며(A8), **런타임은 이 절의 엔드포인트를 호출하지 않는다.**

### 6.1 엔드포인트

```
영문  GET https://www.pathofexile.com/api/trade/data/static
한국  GET https://poe.kakaogames.com/api/trade/data/static
```

인증·토큰·OAuth **불필요**. 둘 다 공개다.

| 관찰 | 함의 |
|---|---|
| **`poe.game.daum.net`은 301이다.** 최종 목적지가 `poe.kakaogames.com` | 옛 도메인을 코드·문서에 남기지 않는다. 리다이렉트를 따라가지 않으면 167바이트짜리 HTML만 받는다 |
| `www.pathofexile.com`은 **커스텀 User-Agent에 403**(Cloudflare, 5,489바이트 HTML), 브라우저 UA로는 200 | 영문 쪽을 자동 취득하려면 UA를 브라우저로 맞춘다. 한국 쪽은 관대하다 |

### 6.2 두 응답이 위치까지 동일하다 — 조인 규칙의 근거

전수 비교 결과다. **이것이 조인 방법을 결정한다.**

| 항목 | EN | KO | 일치 |
|---|---|---|---|
| 그룹 수 | 23 | 23 | ✅ |
| 엔트리 수 | 1,434 | 1,434 | ✅ |
| `id` **순서까지** 동일 | — | — | ✅ (1,434회 단언 전부 통과) |
| 한쪽에만 있는 `id` | 0 | 0 | ✅ |
| `image` 경로 배열 | — | — | ✅ 완전 동일 |
| 공백 `text` | 14 | 14 | — |

스키마·엔트리 수·순서·`id`·이미지 경로가 모두 같고 **`text`만 지역화돼 있다.** 따라서 조인은 **위치(zip) + `id` 동일성 단언**이며 이름 문자열 매칭이 아니다 — 조인 모호성이 구조적으로 존재하지 않는다. 공백 `text`를 양쪽에서 제외한 유효 짝은 **1,412개**다.

> **왜 이름 조인이 아닌가.** 첫 조사는 영문 **표시명**으로 이었고 99.8%가 붙었다. 그 방법도 동작한다 — 동명이 49건 있었고 전부 같은 한글로 수렴해 모호성이 0이었다 — 그러나 **운에 기댄 조인이다.** 표시명은 poe.ninja 슬러그로 넘어가는 마지막 한 단계에서만 쓴다.

### 6.3 조인 사슬

```
KO static.text  ←(엔트리 id·위치)→  EN static.text  ←(영문 표시명)→  poe.ninja items[].id
```

마지막 단계가 §2.0의 최상위 `items` 배열을 쓴다 — `name`이 영문 표시명이고 `id`가 슬러그이므로, 이 절과 §2.0이 같은 배열에서 만난다.

**두 static 파일은 반드시 같은 시점의 것이어야 한다.** 한쪽만 새 리그를 반영하면 위치가 어긋나고 **사전 전체가 조용히 한 칸씩 밀린다.** `id` 동일성 단언이 그것을 잡는 유일한 장치이므로 생성기에서 제거하지 않는다.

### 6.4 KR 지연 — 결측은 결함이 아니다

전체 items 개수가 **EN 6,005 / KO 6,002**로 3건 차이다. **한국 클라이언트가 본섭보다 늦고, 신규 리그 아이템은 한동안 한글 이름이 아예 존재하지 않는다.**

실측 시점의 미해결 2건:

| 슬러그 | 영문 | 카테고리 |
|---|---|---|
| `message-in-a-bottle` | Message in a Bottle | Fragment |
| `reflection-of-the-heart` | Reflection of the Heart | DivinationCard |

앱은 이미 이것을 흡수한다 — 사전에 키가 없으면 `ItemName`이 **④단계에서 API 영문명으로 폴백**하고, 미해결 슬러그는 결함이 아니므로 `Debug`로만 남는다(`02-lld-core.md` §9.4). **없는 항목에 영문을 채워 넣지 않는다.** 폴백이 이미 그 일을 하며, 채워 넣으면 KR이 따라잡았을 때 갱신 대상인지 알 수 없어진다.

### 6.5 적중률 — 실측

| | |
|---|---|
| poe.ninja 아이템 (18 카테고리, `Allflame`) | **970** |
| 사전에 들어간 것 | **968 (99.8%)** |
| 미해결 | 2 (§6.4) |
| 영문명 하나가 서로 다른 한글로 갈리는 경우 | **0** |

`REQUIREMENTS.md` §3이 경고한 문제 — poe.ninja의 한글 라벨이 게임 용어와 다르다(`쇠똥구리` vs `갑충석`) — 는 이 출처에서 원천적으로 발생하지 않는다. GGG가 **한국어 클라이언트 데이터로 직접 생성**하는 값이기 때문이다. 실측 확인: `ambush-scarab` → `매복 갑충석` ✅

### 6.6 아이콘 CDN

`https://web.poecdn.com` + `entries[].image`의 `/gen/image/...` 경로. 인증 불필요.

**basename만으로 저장하면 안 된다.** 고유 경로 675개에 대해 고유 basename은 640개뿐이고, `MapNumbers*` 계열 16개 이름이 서로 다른 이미지 2~4장에 중복으로 걸린다. 충돌하는 basename은 `<이름>__<경로 끝에서 두 번째 세그먼트>.png`로 저장한다.

**아이콘을 쓴다 — A7 닫힘(2026-08-17, FR-04-6).** 슬러그 → 파일명 매핑은 이름 사전과 **같은 조인 사슬**(§6.3)로 빌드 타임에 만들어 커밋한다(`tools/build-icon-manifest.py`). 런타임 CDN 접근은 없다.

점술 카드 468종에 `image`가 없는 것은 결측이 아니다 — **게임에서 점술 카드는 전부 같은 아이콘을 쓴다**(2026-08-16 사용자 확인). A7의 「DivinationCard 전 항목 결측」과 같은 사실을 다른 각도에서 본 것이다.

**그 공용 아이콘의 출처** 【실측 2026-08-17】. `entries[].image`에 없으므로 `/gen/image/...` 경로로는 얻을 수 없다. 원본 아트 경로가 답한다:

```
https://web.poecdn.com/image/Art/2DItems/Divination/InventoryIcon.png   → 200, 78x78 RGBA PNG, 10,836 B
https://web.poecdn.com/gen/image/...InventoryIcon.png (동일 아트의 gen 경로 추정) → 404
```

`data/images/DivinationCard.png`로 저장하고 **점술 카드 슬러그 전체가 이 한 장을 가리킨다.** `--icons`가 쓰는 `curl.cfg`에 이 URL 한 줄이 추가로 들어간다.

**적중률** 【실측 2026-08-17, 사전에 든 968 슬러그 기준】

| | |
|---|---|
| 개별 아이콘을 얻은 슬러그 | **576** |
| 공용 카드 아이콘으로 덮이는 슬러그 | **392** (전부 GGG static의 `Cards` 그룹 — 다른 그룹은 0건) |
| 아이콘이 없는 슬러그 | **0** |

미해결 2건(§6.4)은 이름 사전에도 없으므로 이 셈 밖이며, 이름과 마찬가지로 아이콘도 없다.

**컬러키 충돌은 실측으로 배제했다.** 오버레이는 `LWA_COLORKEY`로 `0x00FF00FF`를 키로 쓴다 — 아이콘 안에 그 색의 불투명 픽셀이 있으면 화면에 구멍이 뚫린다. 675장 전수 스캔 결과 **0개**이며 최근접 픽셀도 거리 4다. 수치와 프로브는 `00-shell-measurements.md` §14. 생성기가 매니페스트를 쓸 때마다 이 스캔을 반복하므로 리그가 바뀌어도 전제가 조용히 깨지지 않는다.

### 6.7 채택하지 않은 대안

| 후보 | 판정 |
|---|---|
| poe.ninja `language=ko` · `lang=ko` · `locale=ko` | **불가.** 200을 주지만 이름이 영문과 한 글자도 다르지 않다(§2.0) |
| Awakened PoE Trade `data/ko/items.ndjson` | 상류가 **비어 있다.** 메인 저장소는 `en`만 채워져 있고 한국어는 커뮤니티 포크 의존. 생성 스크립트가 저장소에 없다 |
| RePoE / GGPK 추출 | 영문 전용. 한글을 얻으려면 **한국 클라이언트 Content.ggpk 수십 GB**가 필요하다 |
| poedb.tw `/kr/` | HTML 스크래핑 + 2차 출처. GGG 공식 API가 있는데 쓸 이유가 없다 |
