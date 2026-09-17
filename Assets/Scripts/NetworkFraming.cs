using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

public class NetworkFraming : MonoBehaviour
{
    //최대 허용 본문 크기
    public const int MaxBodySize = 64 * 1024; //64kb
    //헤더 크기
    private const int HeaderSize = 4;

    // ---------------------------------------------------------------
    // 송신
    // ---------------------------------------------------------------

    //길이 헤더를 붙여 한 메시지를 전송한다.
    public static async Task SendFramedAsync(NetworkStream stream, string message, CancellationToken token)
    {
        byte[] body = Encoding.UTF8.GetBytes(message);

        if (body.Length > MaxBodySize)
            throw new ArgumentException($"본문이 너무 큼: {body.Length} > {MaxBodySize}");

        // 헤더와 본문을 한 배열에 담아 한 번에 Write.
        // 두 번 나눠 쓰면 헤더만 도착하고 본문이 지연되는 구간이 생길 수 있다.
        byte[] packet = new byte[HeaderSize + body.Length]; //패킷 크기 선언
        BitConverter.GetBytes(body.Length).CopyTo(packet, 0); //데이터 길이 헤더로 설정 int = 4 byte
        body.CopyTo(packet, HeaderSize); //데이터 복사

        await stream.WriteAsync(packet, 0, packet.Length, token); //데이터 전송
        await stream.FlushAsync(token); //데이터 밀어내기
    }

    // ---------------------------------------------------------------
    // 수신
    // ---------------------------------------------------------------

    //한 메시지를 읽는다. 상대가 정상 종료하면 null을 반환한다.
    public static async Task<string> ReceiveFramedAsync(NetworkStream stream, CancellationToken token)
    {
        // 1) 헤더 읽기
        byte[] header = new byte[HeaderSize]; //헤더 길이 할당
        if (!await ReadExactAsync(stream, header, HeaderSize, token)) //헤더 읽기
            return null;   // 헤더 끊김 = 종료

        // 2) 길이 검증
        int bodyLength = BitConverter.ToInt32(header, 0); //int로 파싱
        if (bodyLength < 0 || bodyLength > MaxBodySize) //비정상 데이터가 들어오진 않았는지 검증
            throw new InvalidDataException($"비정상 본문 길이: {bodyLength}"); //예외 처리

        if (bodyLength == 0) //데이터 검증
            return string.Empty;   //빈 메시지도 유효한 프레임으로 인정 및 조기 반환

        // 3) 본문을 정확히 읽는다.
        byte[] body = new byte[bodyLength]; //데이터 길이 할당 (음수 혹은 0이 아니며 MaxBodySize 미만 크기의 정수)
        if (!await ReadExactAsync(stream, body, bodyLength, token)) //본문 읽기
            throw new EndOfStreamException("본문 수신 중 연결이 끊김"); //stream이 끊겼을 때 예외 처리

        return Encoding.UTF8.GetString(body); //데이터 반환
    }

    //count 길이만큼 Network stream에서 데이터를 읽는다.
    private static async Task<bool> ReadExactAsync(NetworkStream stream, byte[] buffer, int count, CancellationToken token)
    {
        int offset = 0;

        while (offset < count) //데이터 길이만큼 채워질 때까지 반복
        {
            int read = await stream.ReadAsync(buffer, offset, count - offset, token); //일부만 채워졌을 때 가정으로 전체 데이터 길이(count)에서 이미 받은 데이터 크기만큼(offset)을 뺌

            if (read == 0)
                return false;   // 상대가 연결을 닫음

            offset += read;
        }

        return true;
    }
}
