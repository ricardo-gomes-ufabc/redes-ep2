using System.Net;
using System.Net.Sockets;

namespace EP2;

public enum EstadoConexão
{
    Escuta,
    SynRecebido,
    Estabelecida,
    Fechando,
    Fechada
}

internal class Receiver
{
    private static Canal? _canal;
    private static bool _conexaoAtiva;
    private static EstadoConexão _estadoConexão;

    private static void Main()
    {
        try
        {
            Console.Write("Digite a porta do Receiver: ");

            int porta = Convert.ToInt32(Console.ReadLine());

            IPEndPoint pontoConexao = new IPEndPoint(IPAddress.Any, porta);

            _canal = new Canal(pontoConexaoLocal: pontoConexao);

            _conexaoAtiva = true;
            _estadoConexão = EstadoConexão.Escuta;

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
        while (_conexaoAtiva)
        {
            SegmentoConfiavel segmentoConfiavel = _canal.ReceberSegmento();

            if (segmentoConfiavel == null)
            {
                continue;
            }

            switch (_estadoConexão)
            {
                case EstadoConexão.Escuta:
                    if (segmentoConfiavel is {Syn: true, Ack: false, Fin: false })
                    {

                    }
                    break;
                case EstadoConexão.SynRecebido:
                    break;
                case EstadoConexão.Estabelecida:
                    break;
                case EstadoConexão.Fechando:
                    break;
                case EstadoConexão.Fechada:
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        byte[]? bufferReceptor;

        double timeoutSegundos = 15;

        using (Timer? timer = new Timer(_ => tokenCancelamento.Cancel(), null, TimeSpan.FromSeconds(timeoutSegundos), TimeSpan.FromMilliseconds(-1)))
        {
            try
            {
                while (!tokenCancelamento.IsCancellationRequested)
                {
                    if (_canal != null && _canal._socket.Available == 0 )
                    {
                        Thread.Sleep(100);
                        continue;
                    }

                    timer.Change(TimeSpan.FromSeconds(timeoutSegundos), Timeout.InfiniteTimeSpan);

                    bufferReceptor = _canal?.ReceberMensagem();

                    if (_canal != null && _canal.ProcessarMensagem(bufferReceptor) )
                    {
                        Console.WriteLine($"Mensagem UDP recebida.");

                        _canal?.EnviarMensagem(_canal.GerarMensagemUdp());

                        Console.WriteLine("Mensagem UDP respondida");
                    }
                }
            }
            catch (SocketException e)
            {
                if (e.SocketErrorCode != SocketError.TimedOut)
                {
                    throw;
                }
            }
        }
    }
}
