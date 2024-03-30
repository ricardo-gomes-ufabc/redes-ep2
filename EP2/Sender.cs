using System.Net;
using System.Net.Sockets;

namespace EP2;

public enum EstadoConexaoSender
{
    SynEnviado,
    Estabelecida,
    Fin1,
    Fin2,
    Fechada
}

internal class Sender
{
    private static Canal _canal;
    private static Random? _aleatorio;
    private static EstadoConexaoSender _estadoConexão = EstadoConexaoSender.Fechada;
    private static uint _sequenceNumber = 0;

    private static void Main()
    {
        try
        {
            Console.Write("Digite a porta do Sender: ");

            int portaCliente = Convert.ToInt32(Console.ReadLine());

            IPEndPoint pontoConexaoLocal = new IPEndPoint(IPAddress.Any, portaCliente);

            Console.Write("Digite o IP do Sender: ");

            string? ipServidor = Console.ReadLine();

            Console.Write("Digite a porta do Sender: ");

            int portaServidor = Convert.ToInt32(Console.ReadLine());

            IPEndPoint pontoConexaoRemoto;

            pontoConexaoRemoto = string.IsNullOrEmpty(ipServidor) ? 
                                 new IPEndPoint(IPAddress.Loopback, portaServidor) : 
                                 new IPEndPoint(IPAddress.Parse(ipServidor), portaServidor);

            _canal = new Canal(pontoConexaoRemoto: pontoConexaoRemoto,
                               pontoConexaoLocal: pontoConexaoLocal);

            Console.Write("Digite a mensagem a ser enviada: ");

            string? mensagem = Console.ReadLine();

            Console.Write("Digite a quantidade de mensagens a serem enviadas: ");

            int quantidadeMensagens = Convert.ToInt32(Console.ReadLine());

            EnviarMensagens(quantidadeMensagens, mensagem);

            Console.WriteLine("Sender encerrado.");

            _canal.Fechar();
        }
        catch (Exception e)
        {
            Console.WriteLine(e.Message);
            throw;
        }
    }

    private static void EnviarMensagens(int quantidade, string? mensagem)
    {
        SegmentoConfiavel syn = new SegmentoConfiavel(true,
                                                      false,
                                                      false,
                                                      0)

        if (modoParalelo)
        {
            List<Thread> threads = new List<Thread>();

            for (int i = 0; i < quantidade; i++)
            {
                Thread thread = new Thread(EnvioMensagem);

                threads.Add(thread);

                thread.Start();
            }

            foreach (Thread thread in threads)
            {
                thread.Join();
            }
        }
        else
        {
            for (int i = 0; i < quantidade; i++)
            {
                EnvioMensagem();
            }
        }
    }

    private static void EnvioMensagem()
    {
        _canal?.EnviarMensagem(_canal.GerarMensagemUdp());

        try
        {
            if (_canal != null && _canal.ProcessarMensagem(_canal.ReceberMensagem()))
            {
                Console.WriteLine($"Mensagem de resposta recebida.");
            }
        }
        catch (SocketException e)
        {
            if (e.SocketErrorCode != SocketError.TimedOut)
            {
                throw;
            }

            Console.WriteLine($"Mensagem de resposta nunca chegou.");
        }
    }
}

