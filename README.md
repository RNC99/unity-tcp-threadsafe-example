# Unity TCP Server/Client — Thread-Safe Example

Unity에서 TCP 통신을 구현할 때 발생하는 스레드 경계 문제를
`ConcurrentQueue` 기반 메인 스레드 마샬링으로 해결한 최소 예제입니다.

## 왜 만들었나

Unity의 UI·Transform·GameObject API는 메인 스레드에서만 호출할 수 있습니다.
그러나 TCP 수신은 별도 워커 스레드에서 이루어지므로,
수신 콜백에서 UI를 직접 갱신하면 예외가 발생하거나 조용히 실패합니다.

실무에서 의료기기 관리 프로그램과 VR 클라이언트 간 TCP 통신을 설계하며
같은 문제를 다룬 경험이 있어, 그 구조를 회사 코드 없이 처음부터 다시 작성했습니다.

## 구조

```
[워커 스레드]                                     [메인 스레드]
AcceptLoopAsync  ─┐
ReceiveLoopAsync ─┼─→ ConcurrentQueue<Action> ─→ Update()에서 Dequeue
Log()            ─┘                                    └→ OnLog 이벤트 발행
                                                          └→ UI 갱신
```

- 네트워크 계층은 UI를 참조하지 않고, `Action`을 큐에 넣습니다.
- UI 계층은 스레드를 직접 참조하지 않고, 큐에서 Invoke되는 이벤트를 구독만 합니다.
- 두 계층은 `ConcurrentQueue` 하나로만 연결됩니다.

## 구현 내용

| 항목 | 방식 |
|---|---|
| 접속 수락 | `AcceptTcpClientAsync` 비동기 루프 |
| 수신 | 클라이언트별 독립 `ReceiveLoopAsync` |
| 스레드 경계 | `ConcurrentQueue<Action>` + `Update()` 소비 |
| 종료 | `CancellationTokenSource`로 전체 루프 일괄 취소 |
| 클라이언트 목록 | `lock` 기반 동기화 + 브로드캐스트 시 스냅샷 복사 |
| 정리 | `OnDestroy` / `OnApplicationQuit`에서 소켓 해제 |

## 실행 방법

1. Unity 6 이상에서 프로젝트 열기
2. `TCP_Sample` 재생
3. `[서버 시작]` → `[접속]` → 메시지 입력 후 `[전송]`
4. 서버가 `ECHO|메시지`로 브로드캐스트하는 것을 로그에서 확인

## 알려진 한계

이 예제는 스레드 경계 처리 시연이 목적이므로 다음은 다루지 않습니다.

- 메시지 프레이밍 (TCP는 스트림이므로 실제로는 길이 헤더 필요)
- 재연결 로직
- 인증 및 암호화

## 환경

Unity 6000.0.68f1 / .NET Standard 2.1 / Windows 11