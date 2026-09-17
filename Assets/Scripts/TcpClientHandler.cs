using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

public class TcpClientHandler : MonoBehaviour
{
    [SerializeField] private string host = "127.0.0.1"; //서버 컴퓨터 아이피
    [SerializeField] private int port = 7777; //열린 서버의 포트

    //캐싱
    private TcpClient _client;
    private NetworkStream _stream;
    private CancellationTokenSource _cts;

    private readonly ConcurrentQueue<Action> _mainThreadActions = new(); //워커 스레드에서 메인 스레드로 데이터를 넘겨주는 통로

    public event Action<string> OnLog; //연결 상태 등을 띄워줄 메인 스레드 로그 이벤트

    public bool IsConnected => _client?.Connected ?? false; //서버 연결 상태 반환 boolean

    #region Server Connection

    //서버 연결 시도
    public async void Connect()
    {
        if (IsConnected) return; //중복 실행 방지

        try
        {
            //캐싱
            _cts = new CancellationTokenSource();
            _client = new TcpClient();

            await _client.ConnectAsync(host, port); //연결 시도
            _stream = _client.GetStream();

            Log($"서버 연결 성공 ({host}:{port})"); //연결 상태 출력
            _ = ReceiveLoopAsync(_cts.Token);
        }
        catch (Exception e)
        {
            Log($"연결 실패: {e.Message}");
        }
    }

    //서버 연결 해제
    public void Disconnect()
    {
        //캐싱된 변수들 해제
        _cts?.Cancel();
        _stream?.Close();
        _client?.Close();
        _client = null;
        Log("연결 해제");
    }

    #endregion

    #region WorkerThread

    //서버에 데이터 송신
    public async void Send(string message)
    {
        if (!IsConnected) { Log("미연결 상태"); return; } //미연결 시 반환

        try
        {
            /* NetworkFraming.cs에서 처리
            byte[] data = Encoding.UTF8.GetBytes(message); //데이터 Encoding
            await _stream.WriteAsync(data, 0, data.Length); //전송
            */

            await NetworkFraming.SendFramedAsync(_stream, message, _cts.Token);
            Log($"송신: {message}");
        }
        catch (Exception e)
        {
            Log($"송신 실패: {e.Message}");
        }
    }

    //서버에서 온 데이터 수신
    private async Task ReceiveLoopAsync(CancellationToken token)
    {
        //var buffer = new byte[1024]; //합의된 버퍼 크기 지정 (프레임 설계를 통해 임의값으로 버퍼의 크기를 지정할 필요가 없어짐)

        try
        {
            while (IsConnected && !token.IsCancellationRequested) //연결 상태 확인
            {
                /* 데이터 읽기 및 인코딩 부분 전체 NetworkFraming.cs에서 처리
                int read = await _stream.ReadAsync(buffer, 0, buffer.Length, token); //데이터 읽기
                if (read == 0) break;

                string msg = Encoding.UTF8.GetString(buffer, 0, read); //데이터 Decoding
                */

                string msg = await NetworkFraming.ReceiveFramedAsync(_stream, token);
                if (msg == null) break;

                Log($"수신: {msg}");
            }
        }
        catch (OperationCanceledException) { } //정상 종료
        catch (InvalidDataException e)
        {
            Log($"프로토콜 오류: {e.Message}");
        }
        catch (Exception e)
        {
            Log($"수신 오류: {e.Message}");
        }
        finally
        {
            Log("서버와의 연결이 종료됨");
        }
    }

    #endregion

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

    private void OnDestroy() => Disconnect();
    private void OnApplicationQuit() => Disconnect();
}
