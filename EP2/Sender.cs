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
    private static Canal _canal;
    private static bool _conexaoAtiva;

    private static EstadoConexaoSender _estadoConexao = EstadoConexaoSender.Fechada;

    private static uint _numeroSeq = 0;
    private static uint _numeroAck = 0;

    private static int _timeoutMilissegundos = 30000;
    private static Timer _temporizadorRecebimento;

    private static Dictionary<uint, SegmentoConfiavel> _bufferMensagens = new Dictionary<uint, SegmentoConfiavel>();

    private static uint _totalMensagens;
    private static uint _tamanhoJanela = 20;
    private static uint _base = 1;
    private static uint _proximoSeqNum = 1;

    private static bool _recebendoAcks = false;

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

            _canal = new Canal(pontoConexaoRemoto: pontoConexaoRemoto,
                               pontoConexaoLocal: pontoConexaoLocal);

            Console.Write("Digite a mensagem a ser enviada: ");

            string? mensagem = Console.ReadLine();

            Console.Write("Digite a quantidade de mensagens a serem enviadas: ");

            uint quantidadeMensagens = Convert.ToUInt32(Console.ReadLine());

            CriarBufferMensagens(quantidadeMensagens, mensagem);

            EnviarMensagens();

            Console.WriteLine("Sender encerrado.");

            _canal.Fechar();
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

                    _canal.EnviarSegmento(syn);

                    _estadoConexao = EstadoConexaoSender.SynEnviado;

                    IniciarTemporizador();

                    break;
                }
                case EstadoConexaoSender.SynEnviado:
                {
                    SegmentoConfiavel? synAck = _canal.ReceberSegmento();

                    if (synAck is { Syn: true, Ack: true, Push: false, Fin: false } && synAck.NumSeq == _numeroAck && synAck.NumAck == _numeroSeq + 1)
                    {
                        _numeroSeq = synAck.NumAck;

                        PararTemporizador();

                        ResponderMensagem(synAck);

                        _estadoConexao = EstadoConexaoSender.Estabelecida;
                    }

                    break;
                }
                case EstadoConexaoSender.Estabelecida:
                {
                    lock (_trava)
                    {
                        while (_base != _totalMensagens)
                        {
                            if (_proximoSeqNum < _base + _tamanhoJanela)
                            {
                                for (_proximoSeqNum = _base; _proximoSeqNum < _base + _tamanhoJanela && _proximoSeqNum <= _totalMensagens; _proximoSeqNum++)
                                {
                                    SegmentoConfiavel proximoSegmentoConfiavel = _bufferMensagens[_proximoSeqNum];

                                    proximoSegmentoConfiavel.SetNumAck(_numeroAck);

                                    Task.Run(() =>
                                    {
                                        _canal.EnviarSegmento(proximoSegmentoConfiavel);
                                    });

                                    if (_proximoSeqNum == _base)
                                    {
                                        IniciarTemporizador();
                                    }
                                }

                                _recebendoAcks = true;

                                ReceberRespostas();
                            }
                        }
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

                    _canal.EnviarSegmento(fin);

                    IniciarTemporizador();

                    SegmentoConfiavel? finAck = _canal.ReceberSegmento();

                    if (finAck is { Syn: false, Ack: true, Push: false, Fin: false } && finAck.NumAck == _numeroSeq + 1)
                    {
                        PararTemporizador();

                        _estadoConexao = EstadoConexaoSender.Fin2;
                    }

                    break;
                }
                case EstadoConexaoSender.Fin2:
                {
                    IniciarTemporizador();

                    SegmentoConfiavel? fin = _canal.ReceberSegmento();

                    if (fin is { Syn: false, Ack: false, Push: false, Fin: true } && fin.NumAck == _numeroSeq + 1)
                    {
                        PararTemporizador();

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

    private static void IniciarTemporizador()
    {
        _temporizadorRecebimento = new Timer(_timeoutMilissegundos);
        _temporizadorRecebimento.Elapsed += TemporizadorEncerrado;
        _temporizadorRecebimento.AutoReset = false;
    }

    private static void PararTemporizador()
    {
        _temporizadorRecebimento.Stop();
        _temporizadorRecebimento.Dispose();
    }

    private static void TemporizadorEncerrado(object? state, ElapsedEventArgs elapsedEventArgs)
    {
        lock (_trava)
        {
            PararTemporizador();

            switch (_estadoConexao)
            {
                case EstadoConexaoSender.SynEnviado:
                {
                    _estadoConexao = EstadoConexaoSender.Fechada;

                    break;
                }
                case EstadoConexaoSender.Estabelecida:
                {
                    _recebendoAcks = false;

                    for (uint i = _base; i < _proximoSeqNum; i++)
                    {
                        SegmentoConfiavel proximoSegmentoConfiavel = _bufferMensagens[i];

                            Task.Run(() =>
                        {
                            _canal.EnviarSegmento(proximoSegmentoConfiavel);
                        });

                        if (i == _base)
                        {
                            IniciarTemporizador();
                        }
                    }

                    _recebendoAcks = true;

                    ReceberRespostas();

                    break;
                }
                case EstadoConexaoSender.Fin1:
                    break;
                case EstadoConexaoSender.Fin2:
                    break;
                case EstadoConexaoSender.Fechada: break;
                default: throw new ArgumentOutOfRangeException();
            }
        }
    }

    private static void ResponderMensagem(SegmentoConfiavel segmentoConfiavel)
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

        _canal.EnviarSegmento(ack);
    }

    private static void ReceberRespostas()
    {
        while (_recebendoAcks)
        {
            SegmentoConfiavel? ack = _canal.ReceberSegmento();

            lock (_trava)
            {
                if (ack is { Syn: false, Ack: true, Push: false, Fin: false } && ack.NumSeq == _numeroAck && ack.NumAck == _numeroSeq + 1)
                {
                    _base = ack.NumAck;
                    _numeroSeq = ack.NumAck;

                    if (_base == _proximoSeqNum)
                    {
                        PararTemporizador();

                        _recebendoAcks = false;
                    }
                }
            }
        }
    }
}

