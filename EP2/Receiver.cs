using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Timers;
using Timer = System.Threading.Timer;

namespace EP2;

public enum EstadoConexaoReceiver
{
    Escuta,
    SynRecebido,
    Estabelecida,
    Fechando,
    Fechada
}

internal class Receiver
{
    private static Canal _canal;
    private static bool _conexaoAtiva;

    private static EstadoConexaoReceiver _estadoConexão = EstadoConexaoReceiver.Fechada;

    private static uint _numeroAck = 0;

    private static Timer _temporizadorRecebimento;

    private static int _timeout = 30000;

    private static void Main()
    {
        try
        {
            Console.Write("Digite a porta do Receiver: ");

            int porta = Convert.ToInt32(Console.ReadLine());

            IPEndPoint pontoConexao = new IPEndPoint(IPAddress.Any, porta);

            _canal = new Canal(pontoConexaoLocal: pontoConexao);

            _conexaoAtiva = true;

            ReceberMensagens();

            Console.WriteLine("Receiver encerrado.");

            _canal.Fechar();
        }
        catch (Exception e)
        {
            Console.WriteLine(e.Message);
            throw;
        }
    }

    private static void ReceberMensagens()
    {
        _estadoConexão = EstadoConexaoReceiver.Escuta;

        while (_conexaoAtiva)
        {
            SegmentoConfiavel? segmentoConfiavel = _canal.ReceberSegmento();

            TratarMensagem(segmentoConfiavel);
        }
    }

    private static void TratarMensagem(SegmentoConfiavel? segmentoConfiavel)
    {
        if (segmentoConfiavel == null)
        {
            return;
        }

        switch (_estadoConexão)
        {
            case EstadoConexaoReceiver.Escuta:
                if (segmentoConfiavel is { Syn: true, Ack: false, Fin: false} && segmentoConfiavel.SeqNum == _numeroAck)
                {
                    _estadoConexão = EstadoConexaoReceiver.SynRecebido;

                    _numeroAck = segmentoConfiavel.SeqNum + 1;

                    SegmentoConfiavel synAck = new SegmentoConfiavel(Syn: true,
                                                                     Ack: true,
                                                                     Fin: false,
                                                                     SeqNum: _numeroAck,
                                                                     Data: new byte[] { },
                                                                     CheckSum: new byte[] { });

                    _canal.EnviarSegmento(synAck);

                    _temporizadorRecebimento = new Timer(TemporizadorEncerrado, null, _timeout, Timeout.Infinite);
                }
                break;
            case EstadoConexaoReceiver.SynRecebido:
                if (segmentoConfiavel is { Syn: false, Ack: true, Fin: false })
                {

                }
                break;
            case EstadoConexaoReceiver.Estabelecida:
                if (segmentoConfiavel is { Syn: true, Ack: false, Fin: false })
                {

                }
                break;
            case EstadoConexaoReceiver.Fechando:
                break;
            case EstadoConexaoReceiver.Fechada:
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private static void TemporizadorEncerrado(object? state)
    {
        throw new NotImplementedException();
    }
}
