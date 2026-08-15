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

### 2.1 필드 대응표 — §6 → 실제

| `REQUIREMENTS.md` §6 표기 | 실제 raw 필드 | 비고 |
|---|---|---|
| `id` | `lines[].id` | **동일.** FR-01-5·FR-07-2의 키로 그대로 사용 |
| `name` | `core.items[].name` | **`lines`에 없다.** `core.items`를 `id`로 조인해야 한다. `core.items`는 배열이므로 매핑 시 1회 사전 구축 필요 |
| `value.amount` | `lines[].primaryValue` | |
| `value.currency` | `core.primary` | **항목별이 아니라 응답 전역에 하나.** 실측값 `"chaos"` |
| `valueAlt` | 대응 필드 **없음** | `core.rates` + `core.secondary`로 유도 가능하나 **쓸 필요가 없다** (FR-04-5가 금지) |
| `volume` | `lines[].volumePrimaryValue` | FR-04-1에 따라 미표시 |
| `topPair.currency` | `lines[].maxVolumeCurrency` | **FR-04-3 `자동` 모드의 판단 근거** |
| `topPair.rate` | `lines[].maxVolumeRate` | 검산용. 계산 입력으로는 쓰지 않는다 |
| `changePercent` | `lines[].sparkline.totalChange` | FR-04의 변동률 |
| (§6에 없음) | `core.items[].image` | 아이콘. 상대 경로이므로 `https://poe.ninja` 접두 필요. 1차 범위에서 사용 여부 미정 |
| (§6에 없음) | `core.items[].category` | 아이템→카테고리 역인덱스. **FR-01-1 검색 카탈로그에 유용** |
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
| A1 | 아이템 이름이 `lines`에 없다 | 매핑 계층은 **`core.items` 조인을 반드시 수행**한다. `lines`만 파싱하면 이름이 사라진다 |
| A2 | `core.items`가 배열이다 | 응답당 1회 `id → item` 사전을 구축한 뒤 조인. 선형 탐색 금지 |
| A3 | 기준 통화가 응답 전역(`core.primary`)에 하나뿐이다 | 도메인 모델은 항목별 통화를 갖지 않는다. 카테고리 스냅샷 헤더에 둔다. `core.primary != "chaos"` 인 경우를 **검증하고 거부**할 것 (전제 붕괴 감지) |
| A4 | 리그 목록에 현재 챌린지 리그 플래그가 없다 | 첫 원소 채택 + 방어: 배열이 비었거나, 첫 원소가 `Standard`/`Hardcore` 인 경우를 이상으로 간주하고 사용자에게 리그 명시 선택을 요구 |
| A5 | `valueAlt`가 raw에 없다 | FR-04-5 위반 경로가 구조적으로 부재. 설계는 이 사실을 명시하고 `core.rates` 역수 사용도 함께 금지 |
| A6 | `core.items[].category` 존재 | FR-01-1 카탈로그가 카테고리별 18회 호출을 하더라도, 각 응답이 자기 카테고리를 자기 기술(self-describing)한다 |
| A7 | `image`가 상대 경로 | 사용한다면 `https://poe.ninja` 접두. 1차 범위 사용 여부는 S5에서 결정 |

---

## 5. `REQUIREMENTS.md` 정정 권고

§6 「응답 항목별 필드」 표와 「주의」 1항은 사실과 다르다. 다음 중 하나가 필요하다.

1. **권고** — §6을 이 문서의 §2.1 대응표로 교체하고, 주의 1항을 §3.2 내용으로 정정한다.
2. 또는 §6을 "MCP 경유 관측 기록"으로 성격을 재정의하고, 이 문서를 정식 데이터 계약으로 승격한다.

어느 쪽이든 **설계·구현은 이 문서를 따른다.** `REQUIREMENTS.md` §6의 필드명을 그대로 구현하면 동작하지 않는다.
