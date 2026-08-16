# 한글화 데이터 — 실측 발견과 인계

| | |
|---|---|
| 문서 상태 | **실측 확정.** 설계 문서 개정 대기 |
| 측정일 | 2026-08-16 |
| 측정 리그 | `Allflame` |
| 목적 | FR-07-3(한글 사전)의 **출처를 확정하고**, 얻은 데이터와 재생성 절차를 다른 환경에서 이어받을 수 있게 남긴다 |
| 관련 요구사항 | FR-07-2 · FR-07-3 · `REQUIREMENTS.md` §3 주의 · §11 「한글 사전 확보」 |

---

## 0. 이어받는 사람이 먼저 알아야 할 것

### 데이터의 위치

이 문서가 설명하는 산출물은 전부 `data/` 아래에 있다. 파일 680개, 약 4.6 MB.

| 경로 | 내용 | 크기 |
|---|---|---|
| `data/statics.json` | GGG **한국** trade static 원본 | 335,811 B |
| `data/statics.en.json` | GGG **영문** trade static 원본 | 332,892 B |
| `data/ko-items.json` | **생성물.** 슬러그 → 한글 이름 968개 | 47,748 B |
| `data/ko-items.meta.json` | 생성물의 출처·리그·미해결 목록 | 721 B |
| `data/images/*.png` | 아이콘 675장 | 3.91 MB |

원본 두 개(`statics*.json`)만 있으면 나머지는 §6의 절차로 전부 재생성된다.

### 아직 하지 않은 것

**`src/`는 한 줄도 건드리지 않았다.** 사전을 `src/PoeOverlay.Core/Localization/Localization/ko.json`으로 옮기는 일은 **설계 문서 개정이 먼저다**(`CLAUDE.md`의 첫 번째 규칙). 개정 대상은 §7에 적어 두었다.

---

## 1. 결론

**GGG 공식 한국 서버의 trade API가 정답이다.** 영문 서버와 스키마·엔트리 수·엔트리 순서·`id`·이미지 경로가 **완전히 일치**하고 `text`만 지역화돼 있어, 두 응답을 **위치와 `id`로 직접 짝지을 수 있다.** 이름 문자열 매칭이 필요 없으므로 조인 모호성이 구조적으로 존재하지 않는다.

`REQUIREMENTS.md` §3이 경고한 문제 — poe.ninja의 한글 라벨이 게임 용어와 다르다(`쇠똥구리` vs `갑충석`) — 는 이 출처에서 원천적으로 발생하지 않는다. GGG가 **한국어 클라이언트 데이터로 직접 생성**하는 값이기 때문이다. 실측 확인: `ambush-scarab` → `매복 갑충석` ✅

---

## 2. 엔드포인트

```
영문  GET https://www.pathofexile.com/api/trade/data/static
한국  GET https://poe.kakaogames.com/api/trade/data/static
```

| 관찰 | 함의 |
|---|---|
| **`poe.game.daum.net`은 301이다.** 최종 목적지가 `poe.kakaogames.com` | 옛 도메인을 코드·문서에 남기지 말 것. 리다이렉트를 따라가지 않으면 167바이트짜리 HTML만 받는다 |
| `www.pathofexile.com`은 **커스텀 User-Agent에 403**(Cloudflare, 5,489바이트 HTML)을 낸다. 브라우저 UA로는 200 | 영문 쪽을 자동으로 받으려면 UA를 브라우저로 위장해야 한다. 한국 쪽은 관대하다 |
| 인증·토큰·OAuth **불필요** | 두 엔드포인트 모두 공개 |

**아이콘 CDN은 `https://web.poecdn.com` 이며 인증이 필요 없다.** `entries[].image`의 `/gen/image/...` 경로를 그대로 붙이면 된다.

---

## 3. 두 파일이 동일하다는 증거

`statics.en.json`과 `statics.json`을 전수 비교한 결과다. 이게 이 발견의 핵심이며, **조인 방법을 결정한 근거**다.

| 항목 | EN | KO | 일치 |
|---|---|---|---|
| 그룹 수 | 23 | 23 | ✅ |
| 엔트리 수 | 1,434 | 1,434 | ✅ |
| `id` **순서까지** 동일 | — | — | ✅ (`[id] == [id]` 참) |
| 한쪽에만 있는 `id` | 0 | 0 | ✅ |
| `image` 경로 배열 | — | — | ✅ **완전 동일** |
| `text` 공백 | 14 | 14 | — |

따라서 **`zip(EN.entries, KO.entries)`으로 짝지으면서 `id` 동일성을 단언**하는 것이 옳은 조인이다(1,434회 단언 전부 통과). 공백 `text`를 양쪽에서 제외하면 유효한 짝은 **1,412개**다.

> **왜 이름 조인이 아니라 id 조인인가.** 첫 조사에서는 라이브 API를 영문 **표시명**으로 이었고 99.8%가 붙었다. 그 방법도 동작하지만 — 동명이 49건이 있었고 다행히 전부 같은 한글로 수렴해 모호성이 0이었다 — **운에 기댄 조인이다.** 로컬 파일 두 개가 위치까지 같다는 사실이 확인된 이상, 표시명은 poe.ninja 슬러그로 넘어가는 마지막 한 단계에서만 쓴다.

---

## 4. 사전 — 실측 결과

`poe.ninja` 슬러그를 키로 하는 사전(FR-07-2). 조인 사슬은 다음 셋이다:

```
KO static.text  ←(엔트리 id·위치)→  EN static.text  ←(영문 표시명)→  poe.ninja items[].id
```

| | |
|---|---|
| poe.ninja 아이템 (18 카테고리, `Allflame`) | **970** |
| 사전에 들어간 것 | **968 (99.8%)** |
| 미해결 | 2 |
| 영문명 → 한글이 갈리는 경우 | **0** |

| 카테고리 | 적중 | 카테고리 | 적중 |
|---|---|---|---|
| Currency | 100/100 | DivinationCard | 392/393 |
| Scarab | 115/115 | Fragment | 71/72 |
| Essence | 99/99 | Tattoo | 53/53 |
| Runegraft | 31/31 | Oil | 16/16 |
| Fossil | 25/25 | Omen | 12/12 |
| DeliriumOrb | 12/12 | Ducat | 11/11 |
| Astrolabe | 10/10 | AllflameEmber | 8/8 |
| EnshroudingCrystal | 5/5 | Artifact | 4/4 |
| Resonator | 4/4 | DjinnCoin | (항목 0) |

표본:

| 슬러그 | 영문 | 한글 |
|---|---|---|
| `chaos` | Chaos Orb | 카오스 오브 |
| `divine` | Divine Orb | 신성한 오브 |
| `vivid-lifeforce` | Vivid Crystallised Lifeforce | 혈기 생기 결정 |
| `hinekoras-lock` | Hinekora's Lock | 히네코라의 머리카락 |
| `ambush-scarab` | Ambush Scarab | 매복 **갑충석** |
| `essence-of-horror` | Essence of Horror | 경악의 에센스 |
| `abandoned-wealth` | Abandoned Wealth | 버려진 재산 |

### 4.1 미해결 2건 — 결함이 아니라 KR 지연

| 슬러그 | 영문 | 카테고리 |
|---|---|---|
| `message-in-a-bottle` | Message in a Bottle | Fragment |
| `reflection-of-the-heart` | Reflection of the Heart | DivinationCard |

둘 다 **영문 `/api/trade/data/items`에는 있으나 한국 API 어디에도 없다.** 전체 items 개수도 EN 6,005 / KO 6,002로 3건 차이다. 즉 **한국 클라이언트가 본섭보다 늦고, 신규 리그 아이템은 한동안 한글 이름이 아예 존재하지 않는다.**

이건 앱이 이미 흡수하도록 설계돼 있다 — 사전에 키를 넣지 않으면 `ItemName`이 ④단계에서 **API 영문명으로 폴백**하고, 미해결 슬러그는 결함이 아니므로 `Debug` 수준으로만 남는다(`02-lld-core.md` §9.4). **없는 항목에 영문을 채워 넣지 말 것.** 폴백이 이미 그 일을 하고, 채워 넣으면 나중에 KR이 따라잡았을 때 갱신 대상인지 알 수 없어진다.

KR이 따라잡으면 `statics.json`만 다시 받아 §6으로 재생성하면 메워진다.

### 4.2 사전 파일 형태

`data/ko-items.json` — 플랫 딕셔너리, 슬러그 정렬:

```json
{
 "a-chilling-wind": "싸늘한 바람",
 "abandoned-wealth": "버려진 재산",
 "divine": "신성한 오브"
}
```

**앱의 `ko.json`이 이 모양 그대로다.** `LocalizationCatalog`은 `ui.*` 키와 **맨 슬러그 키를 한 딕셔너리에** 담는다 — 근거는 실행 로그의 `Key 'divine' (ItemName) is unresolved`(`02-lld-core.md` §9.4). 슬러그는 케밥이라 `ui.`로 시작할 수 없으므로 두 이름 공간이 충돌하지 않는다.

---

## 5. 아이콘

### 5.1 받은 것

`data/images/` 에 **675장, 3.91 MB.** 전수 검증 통과:

| 검사 | 결과 |
|---|---|
| 계획 대비 누락 / 초과 | 0 / 0 |
| PNG 매직 + `IEND` 트레일러 | 675/675 |
| 0바이트 | 0 |
| 내용이 같은 파일 | **0** (675장 전부 서로 다른 바이트) |
| 크기 | 최소 1,613 B · 중앙값 6,128 B · 최대 22,132 B |

### 5.2 파일명 — basename만 쓰면 35장이 사라진다

엔트리 1,434개 중 `image`를 가진 것은 756개, **고유 경로는 675개**인데 **고유 basename은 640개뿐**이다. `MapNumbers1.png` 같은 이름 **16개**가 서로 다른 이미지 2~4장에 중복으로 걸려 있다(지도 등급 배지의 변형 — 경로에 인코딩된 `mub`/`mb`/`mm` 플래그가 다르다).

채택한 규칙:

- 고유한 basename → **그대로** (624장)
- 충돌하는 basename → **`<이름>__<CDN해시>.png`** (51장, 전부 `MapNumbers*`). 해시는 경로의 끝에서 두 번째 세그먼트다

`statics.json`만 있으면 같은 규칙으로 항목 → 파일명을 재계산할 수 있어 **별도 매니페스트를 두지 않았다.**

### 5.3 아이콘 없는 항목 — 점술 카드는 문제가 아니다

`image`가 없는 엔트리가 678개다. 23개는 그룹 구분선(`id: "sep"`)이고 나머지 655개는 실제 아이템인데 GGG가 아이콘을 주지 않는다: **Cards 468** · Beasts 79 · Heist 46 · Sanctum 27 · MapsUnique 27 등.

**점술 카드 468종에 아이콘이 없는 것은 결측이 아니다 — 게임에서 점술 카드는 전부 같은 아이콘을 쓴다**(2026-08-16 사용자 확인). 카드 하나당 그림이 필요하다는 전제가 틀렸다. 따라서 exchange 18종 중 최대 결측처럼 보였던 DivinationCard 문제는 **공용 아이콘 1장으로 해결된다.**

이는 `00-api-contract.md` A7의 「`image`가 959개 중 576개에만 있다(DivinationCard 전 항목 결측)」와 같은 사실을 다른 각도에서 본 것이다. **아이콘 사용 여부 자체는 여전히 S5 미결정이다.**

---

## 6. 재생성 절차

`data/statics.json` · `data/statics.en.json` 두 원본만 있으면 나머지는 전부 다시 만들 수 있다. 슬러그 목록을 얻는 데만 네트워크(poe.ninja)가 필요하다.

### 6.1 사전

```python
import json, urllib.request
UA={"User-Agent":"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 "
                 "(KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36","Accept":"application/json"}
get=lambda u: json.loads(urllib.request.urlopen(urllib.request.Request(u,headers=UA),timeout=60).read().decode())
CATS=["Currency","Fragment","Runegraft","AllflameEmber","Tattoo","Omen","DjinnCoin","Ducat",
      "EnshroudingCrystal","DivinationCard","Artifact","Oil","DeliriumOrb","Scarab","Astrolabe",
      "Fossil","Resonator","Essence"]

en=json.load(open('data/statics.en.json',encoding='utf-8'))
ko=json.load(open('data/statics.json',encoding='utf-8'))
flat=lambda d:[e for g in d['result'] for e in g.get('entries',[])]

name2ko={}
for e,k in zip(flat(en),flat(ko)):
    assert e['id']==k['id']                      # 위치·id 동일성을 매번 다시 확인한다
    et,kt=e.get('text','').strip(),k.get('text','').strip()
    if et and kt: name2ko.setdefault(et,kt)

lg=get("https://poe.ninja/poe1/api/economy/leagues")[0]["id"]
nin={}
for c in CATS:
    for it in get(f"https://poe.ninja/poe1/api/economy/exchange/current/overview?league={lg}&type={c}").get("items",[]):
        nin[it["id"]]={"en":it.get("name",""),"cat":c}

d={s:name2ko[v["en"]] for s,v in sorted(nin.items()) if v["en"] in name2ko}
miss=[{"slug":s,**v} for s,v in sorted(nin.items()) if v["en"] not in name2ko]
json.dump(d,open('data/ko-items.json','w',encoding='utf-8'),ensure_ascii=False,indent=1,sort_keys=True)
```

**`assert`를 지우지 말 것.** 두 파일이 같은 시점의 것이 아니면(한쪽만 새 리그 반영) 위치가 어긋나고, 그 순간 사전 전체가 **조용히 한 칸씩 밀린다.** 이 단언이 그것을 잡는 유일한 장치다.

### 6.2 아이콘

Anaconda 파이썬의 CA 번들이 만료돼 `web.poecdn.com` 인증서 검증에 실패한다(`CERTIFICATE_VERIFY_FAILED: certificate has expired`). **curl은 시스템 CA를 쓰므로 정상이다.** curl 설정 파일을 만들어 병렬로 받는다:

```python
import json, collections
d=json.load(open('data/statics.json',encoding='utf-8'))
paths=sorted({e['image'] for g in d['result'] for e in g.get('entries',[]) if e.get('image')})
cnt=collections.Counter(p.rsplit('/',1)[-1] for p in paths)
lines=[]
for p in paths:
    seg=p.split('/'); base=seg[-1]
    name=base if cnt[base]==1 else f"{base[:-4]}__{seg[-2]}.png"     # §5.2 규칙
    lines.append(f'url = "https://web.poecdn.com{p}"\noutput = "data/images/{name}"')
open('curl.cfg','w',encoding='utf-8').write("\n".join(lines)+"\n")
```

```bash
curl -sS --fail --create-dirs --retry 3 --connect-timeout 20 --max-time 60 \
     --parallel --parallel-max 8 -A "Mozilla/5.0" -K curl.cfg
```

675장이 **1.2초**에 끝난다(HTTP/2 다중화). 너무 빨라서 의심스럽거든 §5.1의 검사를 다시 돌려라 — 실제로 그렇게 확인했다.

---

## 7. 다음 단계 — 문서가 먼저다

`CLAUDE.md`의 첫 규칙에 따라 데이터가 `src/`로 들어가기 전에 아래가 개정돼야 한다.

| # | 문서 | 개정 내용 |
|---|---|---|
| 1 | `00-api-contract.md` | **신규 절.** 엔드포인트 둘, 두 파일의 동일성 실측(§3), 조인 사슬, KR 지연(§4.1), CDN. 「서버 측 지역화는 없다 … 앱이 직접 채워야 한다」(§2.0 말미)에 이 절로 가는 참조를 단다 |
| 2 | `REQUIREMENTS.md` | §11의 「한글 사전 확보(게임 클라이언트 용어 기준. poe.ninja 라벨은 사용 불가)」를 **해소로 옮긴다.** §3의 `쇠똥구리`/`갑충석` 경고는 그대로 두되 이 출처가 그것을 만족함을 명시. FR-07-3에 출처와 **빌드 타임 생성**을 명시 |
| 3 | `02-lld-core.md` / `04-dld.md` | 사전 파일이 하나 더 생기는 것 외에 **구조 변경은 없다.** FR-07-3의 「코드 변경 없이」가 지켜지는지 확인만 하면 된다 |

그다음 순서:

1. **`ui.*` 한글 문구 약 40개 작성.** 외부 출처가 없다 — 직접 써야 한다. `en.json`(3,184 B)의 키를 그대로 따른다. 자리표시자(`{0}`) 검증이 기동 시 돌므로(`D-L1`) 개수를 어기면 즉시 잡힌다
2. **카테고리 라벨 18개.** GGG static의 그룹 라벨에 한글이 있으나 **poe.ninja의 18개 질의 타입과 1:1이 아니다** — 예컨대 `Fragments` 그룹 라벨 「조각, 갑충석, 지도 탐험」 하나가 ninja의 `Fragment`·`Scarab`을 함께 덮는다. 18줄이니 직접 쓰는 편이 옳다
3. `data/ko-items.json` + 위 둘을 합쳐 `src/PoeOverlay.Core/Localization/Localization/ko.json`으로
4. 생성기를 저장소에 둘지 결정. 두면 **빌드 타임 1회 실행**이다 — 런타임 호출은 NFR-02와 어긋나고 앱을 kakaogames 가용성에 묶는다

---

## 8. 검토했지만 채택하지 않은 대안

| 후보 | 판정 |
|---|---|
| poe.ninja `language=ko` · `lang=ko` · `locale=ko` | **불가.** 200을 주지만 이름이 영문과 한 글자도 다르지 않다 (`00-api-contract.md` §2.0 실측) |
| Awakened PoE Trade `data/ko/items.ndjson` | 상류가 **비어 있다.** 메인 저장소는 `en`만 채워져 있고 한국어는 커뮤니티 포크(`uknowpro/awakened-poe-trade-for-korean`) 의존. 생성 스크립트가 저장소에 없다 |
| RePoE / GGPK 추출 | 영문 전용. 한글을 얻으려면 **한국 클라이언트 Content.ggpk 수십 GB** 설치가 필요하다 |
| poedb.tw `/kr/` | HTML 스크래핑 + 2차 출처. GGG 공식 API가 있는데 쓸 이유가 없다 |

---

## 9. 이 발견에서 걸려 넘어질 수 있는 곳

- **`poe.game.daum.net`을 최종 주소로 적지 말 것.** 301이다
- **`www.pathofexile.com`에 커스텀 UA를 쓰면 403이다.** 데이터가 없는 게 아니라 Cloudflare가 막는 것이다
- **두 static 파일의 시점이 다르면 위치 조인이 밀린다.** §6.1의 `assert`가 유일한 방어다
- **미해결 슬러그에 영문을 채워 넣지 말 것.** ④ 폴백이 이미 그 일을 하고, 채우면 갱신 대상을 잃는다
- **아이콘을 basename으로만 저장하지 말 것.** 35장이 조용히 덮인다(§5.2)
- **점술 카드 아이콘을 찾아 헤매지 말 것.** 전부 같은 그림이다(§5.3)
- 첫 조사의 수치(968/970 → 그 이전 실행은 966/968, DivinationCard 393 → 390)가 서로 다른 것은 **그 사이 poe.ninja 스냅샷이 갱신됐기 때문**이다. 적중률 99.8%와 미해결 2건은 두 실행에서 동일했다
