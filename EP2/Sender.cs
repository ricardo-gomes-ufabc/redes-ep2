using System.Net;
using System.Text;
using System.Threading;
using System.Timers;
using Timer = System.Timers.Timer;


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
    private static Threads _threads;
    private static ElapsedEventHandler _evento = new ElapsedEventHandler(TemporizadorEncerrado);

    private static bool _conexaoAtiva;

    private static EstadoConexaoSender _estadoConexao = EstadoConexaoSender.Fechada;

    private static uint _numeroSeq = 0;
    private static uint _numeroAck = 0;

    private static Dictionary<uint, SegmentoConfiavel> _bufferMensagens = new Dictionary<uint, SegmentoConfiavel>();

    private static uint _totalMensagens;
    private static uint _tamanhoJanela = 20;
    private static uint _base = 1;
    private static uint _proximoSeqNum = 1;

    private static bool _recebendoAcks = false;
    private static bool _reeviandoJanela = false;

    private static object _trava = new object();
    

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

            _threads = new Threads(pontoConexaoRemoto: pontoConexaoRemoto,
                                   pontoConexaoLocal: pontoConexaoLocal);

            _threads.ConfigurarTemporizador(TemporizadorEncerrado);

            Console.Write("Digite a mensagem a ser enviada: ");

            string? mensagem = Console.ReadLine();

            Console.Write("Digite a quantidade de mensagens a serem enviadas: ");

            uint quantidadeMensagens = Convert.ToUInt32(Console.ReadLine());

            CriarBufferMensagens(quantidadeMensagens, mensagem);

            EnviarMensagens();

            Console.WriteLine("Sender encerrado.");

            _threads.Fechar();
        }
        catch (Exception e)
        {
            Console.WriteLine(e.Message);
            throw;
        }
    }

    private static void EnviarMensagens()
    {
        _conexaoAtiva = true;

        while (_conexaoAtiva)
        {
            switch (_estadoConexao)
            {
                case EstadoConexaoSender.Fechada:
                {
                    SegmentoConfiavel syn = new SegmentoConfiavel(syn: true,
                                                                  ack: false,
                                                                  push: false,
                                                                  fin: false,
                                                                  numSeq: _numeroSeq,
                                                                  numAck: _numeroAck,
                                                                  data: Array.Empty<byte>(),
                                                                  checkSum: Array.Empty<byte>());

                    _threads.EnviarSegmento(syn);

                    _estadoConexao = EstadoConexaoSender.SynEnviado;

                    //_threads.IniciarTemporizador();

                    break;
                }
                case EstadoConexaoSender.SynEnviado:
                {
                    SegmentoConfiavel? synAck = _threads.ReceberSegmento();

                    if (synAck is { Syn: true, Ack: true, Push: false, Fin: false } && synAck.NumSeq == _numeroAck && synAck.NumAck == _numeroSeq + 1)
                    {
                        //_threads.PararTemporizador();

                        _numeroSeq = synAck.NumAck;

                        ResponderMensagem(synAck);

                        _estadoConexao = EstadoConexaoSender.Estabelecida;
                    }
                    else
                    {
                        Thread.Sleep(5000);
                    }

                    break;
                }
                case EstadoConexaoSender.Estabelecida:
                {
                    while (_reeviandoJanela)
                    {
                        string identificadores = String.Join(',', Enumerable.Range((int)_base, (int)_proximoSeqNum - 1));

                        Console.WriteLine($"Timeout, reenviando mensagens com identificadores {identificadores}");

                        for (uint i = _base; i < _proximoSeqNum; i++)
                        {
                            SegmentoConfiavel proximoSegmentoConfiavel = _bufferMensagens[i];

                            _threads.EnviarSegmento(proximoSegmentoConfiavel);

                            if (i == _base)
                            {
                                _threads.IniciarTemporizador();
                            }
                        }

                        _recebendoAcks = true;

                        ReceberRespostas();
                    }

                    while (_base <= _totalMensagens && !_reeviandoJanela)
                    {
                        lock (_trava)
                        {
                            if (_proximoSeqNum >= _base + _tamanhoJanela) continue;

                            for (_proximoSeqNum = _base;
                                 _proximoSeqNum < _base + _tamanhoJanela && _proximoSeqNum <= _totalMensagens;
                                 _proximoSeqNum++)
                            {
                                SegmentoConfiavel proximoSegmentoConfiavel = _bufferMensagens[_proximoSeqNum];

                                proximoSegmentoConfiavel.SetNumAck(_numeroAck);

                                _threads.EnviarSegmento(proximoSegmentoConfiavel);

                                if (_proximoSeqNum == _base)
                                {
                                    _threads.IniciarTemporizador();
                                }
                            }

                            _recebendoAcks = true;
                        }

                        ReceberRespostas();
                    }

                    if (_base > _totalMensagens)
                    {
                        _estadoConexao = EstadoConexaoSender.Fin1;
                    }
                    
                    break;
                }
                case EstadoConexaoSender.Fin1:
                {
                    SegmentoConfiavel fin = new SegmentoConfiavel(syn: false,
                                                                  ack: false,
                                                                  push: false,
                                                                  fin: true,
                                                                  numSeq: _numeroSeq,
                                                                  numAck: _numeroAck,
                                                                  data: Array.Empty<byte>(),
                                                                  checkSum: Array.Empty<byte>());

                    _threads.EnviarSegmento(fin);

                    _threads.IniciarTemporizador();

                    SegmentoConfiavel? finAck = _threads.ReceberSegmento();

                    if (finAck is { Syn: false, Ack: true, Push: false, Fin: false } && finAck.NumAck == _numeroSeq + 1)
                    {
                        _threads.PararTemporizador();

                        _estadoConexao = EstadoConexaoSender.Fin2;

                        _numeroSeq = finAck.NumAck;
                    }

                    break;
                }
                case EstadoConexaoSender.Fin2:
                {
                    _threads.IniciarTemporizador();

                    SegmentoConfiavel? fin = _threads.ReceberSegmento();

                    if (fin is { Syn: false, Ack: false, Push: false, Fin: true } && fin.NumAck == _numeroSeq)
                    {
                        _threads.PararTemporizador();

                        ResponderMensagem(fin);

                        _estadoConexao = EstadoConexaoSender.Fechada;

                        _conexaoAtiva = false;
                    }

                    break;
                }
                default: throw new ArgumentOutOfRangeException();
            }
        }

        Thread.Sleep(millisecondsTimeout: 15000);
    }

    private static void CriarBufferMensagens(uint quantidade, string? mensagem)
    {
        _totalMensagens = quantidade;

        for (uint i = 1; i <= quantidade; i++)
        {
            SegmentoConfiavel segmentoMensagem = new SegmentoConfiavel(syn: false,
                                                                       ack: false,
                                                                       push: true,
                                                                       fin: false,
                                                                       numSeq: i,
                                                                       numAck: _numeroAck,
                                                                       data: Encoding.UTF8.GetBytes(mensagem),
                                                                       checkSum: Array.Empty<byte>());

            _bufferMensagens.Add(i, segmentoMensagem);
        }
    }

    private static void TemporizadorEncerrado(object? state, ElapsedEventArgs elapsedEventArgs)
    {
        _threads.PararTemporizador();

        switch (_estadoConexao)
        {
            case EstadoConexaoSender.SynEnviado:
            {
                lock (_trava)
                {
                    _estadoConexao = EstadoConexaoSender.Fechada;

                    _threads.CancelarRecebimento();

                    break;
                }
            }
            case EstadoConexaoSender.Estabelecida:
            {
                lock (_trava)
                {
                    _recebendoAcks = false;
                    _reeviandoJanela = true;

                    _threads.CancelarRecebimento();
                }

                break;
            }
            case EstadoConexaoSender.Fin1: break;
            case EstadoConexaoSender.Fin2: break;
            case EstadoConexaoSender.Fechada: break;
            default: throw new ArgumentOutOfRangeException();
        }
    }

    private static void ResponderMensagem(SegmentoConfiavel? segmentoConfiavel)
    {
        _numeroAck = segmentoConfiavel.NumSeq + 1;

        SegmentoConfiavel ack = new SegmentoConfiavel(syn: false,
                                                      ack: true,
                                                      push: false,
                                                      fin: false,
                                                      numSeq: _numeroSeq,
                                                      numAck: _numeroAck,
                                                      data: Array.Empty<byte>(),
                                                      checkSum: Array.Empty<byte>());

        _threads.EnviarSegmento(ack);
    }

    private static void ReceberRespostas()
    {
        while (_recebendoAcks)
        {
            SegmentoConfiavel? ack = _threads.ReceberSegmento();

            lock (_trava)
            {
                if (ack is { Syn: false, Ack: true, Push: false, Fin: false } && ack.NumSeq == _numeroAck && ack.NumAck == _numeroSeq + 1)
                {
                    Console.WriteLine($"Mensagem id {ack.NumAck} recebida");

                    _base = ack.NumAck;
                    _numeroSeq = ack.NumAck;

                    if (_base == _proximoSeqNum)
                    {
                        _threads.PararTemporizador();

                        _recebendoAcks = false;

                        if (_reeviandoJanela)
                        {
                            _reeviandoJanela = false;
                        }
                    }
                }
            }
        }
    }
}

