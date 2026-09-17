using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using UnityEngine;

public class TcpServer : MonoBehaviour
{
    //서버를 열 네트워크 포트
    [SerializeField] private int port = 7777;

    //신호를 받을 Listner
    private TcpListener _listener;
    //연결이나 서버를 닫을 때 사용할 취소 토큰
    private CancellationTokenSource _cts;

    //서버에 접속할 클라이언트 리스트
    private readonly List<TcpClient> _clients = new();
    private readonly object _clientsLock = new();

    //워커 스레드에서 메인 스레드로 데이터를 넘겨주는 통로
    private readonly ConcurrentQueue<Action> _mainThreadActions = new();

    //연결 상태등을 띄워줄 메인 스레드 로그 이벤트
    public event Action<string> OnLog;

    //서버 열림 상태 확인용 boolean
    public bool IsRunning { get; private set; }

    // ---------------------------------------------------------------
    // 서버 관련 메쏘드
    // ---------------------------------------------------------------

    #region Server

    //서버 열기
    public void StartServer()
    {
        if (IsRunning) return; //이미 서버가 열려 있을 때는 메쏘드를 실행하지 않음

        try //방어코드
        {
            //서버 변수들 새로 할당 및 Listner 실행
            _cts = new CancellationTokenSource();
            _listener = new TcpListener(IPAddress.Any, port);
            _listener.Start();
            IsRunning = true; //메쏘드 중복 실행 방어용

            Log($"서버 시작 (port {port})"); //서버 열었음 로그 표시
            _ = AcceptLoopAsync(_cts.Token); //워커 스레드 열기: 클라이언트 연결 메쏘드 실행
        }
        catch (Exception e)
        {
            Log($"서버 시작 실패: {e.Message}"); //서버 열기 실패 로그 출력
        }
    }

    //서버 중지
    public void StopServer()
    {
        if (!IsRunning) return; //서버가 동작하지 않는 상태라면 반환

        IsRunning = false; //메쏘드 중복 실행 방어용
        _cts?.Cancel(); //워커 스레드 중지

        //클라이언트 연결 해제 및 리스트 초기화
        lock (_clientsLock)
        {
            foreach (var c in _clients) c.Close();
            _clients.Clear();
        }

        _listener?.Stop(); //Listner 중지
        Log("서버 중지"); //서버 중지 로그 출력
    }

    #endregion

    // ---------------------------------------------------------------
    // 데이터 송수신 및 클라이언트 연결 루프 (워커 스레드)
    // ---------------------------------------------------------------

    #region WorkerThread

    //클라이언트 신규 연결 확인
    private async Task AcceptLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested) //취소 확인
        {
            try //방어 코드
            {
                var client = await _listener.AcceptTcpClientAsync(); //클라이언트 -> Listner 통신 연결 대기

                lock (_clientsLock) _clients.Add(client); //신규 연결 성공 시 클라이언트 추가
                Log($"클라이언트 접속 (현재 {ClientCount}명)"); //클라이언트 연결 성공 로그 출력

                _ = ReceiveLoopAsync(client, token); //워커 스레드 열기: 클라이언트 통신 대기 메쏘드
            }
            catch (ObjectDisposedException) //연결 해제된 클라이언트나 Listner에 접근하는 경우 실행. 보통 StopServer()로 인해 Listner가 Stop된 상태
            {
                break; //Loop 탈출
            }
            catch (Exception e) //그 외 예외 상황
            {
                Log($"Accept 오류: {e.Message}"); //에러 상황 로그 출력
                break;
            }
        }
    }

    //Client가 보내온 데이터를 받아서 클라이언트에게 받았다는 신호 반환
    private async Task ReceiveLoopAsync(TcpClient client, CancellationToken token)
    {
        var stream = client.GetStream(); //클라이언트의 Network Stream 가져오기
        //var buffer = new byte[1024]; 클라이언트와 합의된 크기의 버퍼 생성 (네트워크 프레이밍 기능 추가로 인한 삭제)

        try
        {
            while (client.Connected && !token.IsCancellationRequested) //미연결 상태 및 취소 확인
            {
                /* NetworkFraming.cs에서 길이에 맞게 데이터를 가져오는 방식으로 변경
                int read = await stream.ReadAsync(buffer, 0, buffer.Length, token); //Network Stream 읽기
                if (read == 0) break; //데이터 없음 반환
                */

                //string msg = Encoding.UTF8.GetString(buffer, 0, read).Trim(); //데이터 string Decoding (Encoding 또한 NetworkFraming.cs에서 진행하도록 변경)
                string msg = await NetworkFraming.ReceiveFramedAsync(stream, token); //프레임 하나를 읽을 때까지 대기
                if (msg == null) break;

                Log($"수신: {msg}"); //데이터 로그 출력

                await BroadcastAsync($"ECHO|{msg}", token); //클라이언트에게 받았다는 신호 전달
            }
        }
        catch (OperationCanceledException) { /* 정상 취소 */ }
        catch (InvalidDataException e)
        {
            // 프로토콜 위반 종료.
            Log($"프로토콜 오류: {e.Message}");
        }
        catch (Exception e)
        {
            Log($"수신 오류: {e.Message}");
        }
        finally //정상 취소 혹은 예외 오류 모두 클라이언트 해제 과정 진행
        {
            lock (_clientsLock) _clients.Remove(client);
            client.Close();
            Log($"클라이언트 해제 (현재 {ClientCount}명)");
        }
    }

    //서버가 클라이언트에게 데이터 송신
    private async Task BroadcastAsync(string message, CancellationToken token)
    {
        //byte[] data = Encoding.UTF8.GetBytes(message); //보낼 메세지 Encoding (NetworkFraming.cs에서 데이터 불러오는 과정 처리)

        List<TcpClient> snapshot;
        lock (_clientsLock) snapshot = new List<TcpClient>(_clients); //클라이언트 리스트 캐싱

        foreach (var c in snapshot)
        {
            try
            {
                if (c.Connected)
                    //await c.GetStream().WriteAsync(data, 0, data.Length, token); //데이터 전송 (NetworkFraming.cs에서 처리하도록 변경)
                    await NetworkFraming.SendFramedAsync(c.GetStream(), message, token);
            }
            catch { /* 개별 전송 실패는 무시하고 계속 */ }
        }
    }

    private int ClientCount
    {
        get { lock (_clientsLock) return _clients.Count; }
    }

    #endregion

    // ---------------------------------------------------------------
    // 스레드 경계 처리 워커 스레드 -> 이벤트 활성화 및 Queue 비우기
    // ---------------------------------------------------------------

    #region Event

    //로그 이벤트 활성화
    private void Log(string message)
    {
        _mainThreadActions.Enqueue(() => OnLog?.Invoke(message)); //Queue가 쌓이면 이벤트 출력
    }


    private void Update()
    {
        // 큐에 쌓인 작업을 메인 스레드에서 비운다.
        while (_mainThreadActions.TryDequeue(out var action))
        {
            action?.Invoke(); //쌓인 Queue를 비우고 이벤트 출력
        }
    }

    #endregion

    #region ETC

    //프로그램 종료 시 서버 중지
    private void OnDestroy() => StopServer();
    private void OnApplicationQuit() => StopServer();

    #endregion
}
