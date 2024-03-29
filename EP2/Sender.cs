using System.Net;
using System.Net.Sockets;

namespace EP2;

internal class Sender
{
    private static Canal? _canal;
    private static Random? _aleatorio;

    private static void Main()
    {
        try
        {
            Console.Write("Digite a porta do Sender: ");

            int portaCliente = Convert.ToInt32(Console.ReadLine());

            IPEndPoint pontoConexaoLocal = new IPEndPoint(IPAddress.Any, portaCliente);

            Console.Write("Digite o IP do Receiver: ");

            string? ipServidor = Console.ReadLine();

            Console.Write("Digite a porta do Receiver: ");

            int portaServidor = Convert.ToInt32(Console.ReadLine());

            IPEndPoint pontoConexaoRemoto;

            pontoConexaoRemoto = string.IsNullOrEmpty(ipServidor) ? 
                                 new IPEndPoint(IPAddress.Loopback, portaServidor) : 
                                 new IPEndPoint(IPAddress.Parse(ipServidor), portaServidor);

            _canal = new Canal(pontoConexaoRemoto: pontoConexaoRemoto,
                               pontoConexaoLocal: pontoConexaoLocal);

            Console.Write("Deseja enviar de forma paralela [S/N]?: ");

            string? paralelismo = Console.ReadLine();
            bool modoParalelo = !string.IsNullOrEmpty(paralelismo) && paralelismo.ToLower() == "s";

            Console.Write("Digite a quantidade de mensagens a serem enviadas: ");

            int quantidadeMensagens = Convert.ToInt32(Console.ReadLine());

            EnviarMensagens(quantidadeMensagens, modoParalelo);

            Console.WriteLine("Cliente encerrado.");

            _canal.Fechar();
        }
        catch (Exception e)
        {
            Console.WriteLine(e.Message);
            throw;
        }
    }

    private static void EnviarMensagens(int quantidade, bool modoParalelo)
    {
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

