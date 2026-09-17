using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class DemoUI : MonoBehaviour
{
    [Header("Network")] //네트워크 코드 캐싱
    [SerializeField] private TcpServer server;
    [SerializeField] private TcpClientHandler client;

    [Header("UI")] //데모 UI 오브젝트들 캐싱
    [SerializeField] private Button startServerButton;
    [SerializeField] private Button stopServerButton;
    [SerializeField] private Button connectButton;
    [SerializeField] private Button sendButton;
    [SerializeField] private TMP_InputField inputField;
    [SerializeField] private TMP_Text logText;

    [Header("Framing Test")] //네트워크 프레이밍 관련 테스트용 버튼
    [SerializeField] private Button burstTestButton;   //뭉침 테스트
    [SerializeField] private Button largeTestButton; //잘림 테스트

    private const int MaxLines = 10; //최대 로그 줄 수
    private readonly System.Collections.Generic.Queue<string> _lines = new(); //네트워크 코드의 OnLog에 구독시킬 Queue

    void Start()
    {
        //로그 이벤트 구독
        server.OnLog += msg => AppendLog($"[Server] {msg}"); 
        client.OnLog += msg => AppendLog($"[Client] {msg}");

        //버튼 OnClick 이벤트 할당
        startServerButton.onClick.AddListener(server.StartServer);
        stopServerButton.onClick.AddListener(server.StopServer);
        connectButton.onClick.AddListener(client.Connect);
        sendButton.onClick.AddListener(SendMessageFromInput);

        burstTestButton.onClick.AddListener(SendBurst);
        largeTestButton.onClick.AddListener(SendLarge);
    }

    //Input Field의 값을 클라이언트에게 전송
    private void SendMessageFromInput()
    {
        if (string.IsNullOrWhiteSpace(inputField.text)) return;
        client.Send(inputField.text);
        inputField.text = "";
    }

    //TMP에 로그 띄우기
    private void AppendLog(string line)
    {
        _lines.Enqueue($"{System.DateTime.Now:HH:mm:ss} {line}");
        while (_lines.Count > MaxLines) _lines.Dequeue(); //MaxLines 보다 줄 수가 많으면 오래된 값부터 삭제

        logText.text = string.Join("\n", _lines);
    }

    //뭉침 테스트: 지연 없이 연속 전송해 OS가 묶어 보내도록 유도
    private void SendBurst()
    {
        for (int i = 1; i <= 20; i++)
            client.Send($"MSG-{i:D2}");
    }

    //잘림 테스트: 버퍼 크기를 넘는 한글 메시지 전송
    private void SendLarge()
    {
        // 한글 1자 = UTF-8 3바이트. 2000자 = 6000바이트 → 반드시 여러 번 나뉘어 도착
        string large = new string('한', 2000);
        client.Send($"LARGE|{large.Length}자|{large}");
    }
}
