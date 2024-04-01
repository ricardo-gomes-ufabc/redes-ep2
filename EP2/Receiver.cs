using System.Net;
using System.Timers;
using Timer = System.Timers.Timer;


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
    private static Threads _threads;
    private static bool _conexaoAtiva;

    private static EstadoConexaoReceiver _estadoConexao = EstadoConexaoReceiver.Fechada;

    private static uint _numeroSeq = 0;
    private static uint _numeroAck = 0;

    private static int _timeoutMilissegundos = 5000;
    private static Timer _temporizadorRecebimento;

    private static CancellationTokenSource _tockenCancelamentoRecebimento = new CancellationTokenSource();

    private static object _trava = new object();

    private static void Main()
    {
        try
        {
            Console.Write("Digite a porta do Receiver: ");

            int porta = Convert.ToInt32(Console.ReadLine());

            IPEndPoint pontoConexao = new IPEndPoint(IPAddress.Any, porta);

            _threads = new Threads(pontoConexaoLocal: pontoConexao);

            _conexaoAtiva = true;

            ReceberMensagens();

            Console.WriteLine("Receiver encerrado.");

            _threads.Fechar();
        }
        catch (Exception e)
        {
            Console.WriteLine(e.Message);
            throw;
        }
    }

    private static void ReceberMensagens()
    {
        _estadoConexao = EstadoConexaoReceiver.Escuta;

        while (_conexaoAtiva)
        {
            TratarMensagem(_threads.ReceberSegmento());
        }

        Thread.Sleep(millisecondsTimeout: 15000);
    }

    private static void TratarMensagem(SegmentoConfiavel? segmentoConfiavel)
    {
        lock (_trava)
        {
            if (segmentoConfiavel == null)
            {
                return;
            }

            switch (_estadoConexao)
            {
                case EstadoConexaoReceiver.Escuta:
                {
                    if (segmentoConfiavel is { Syn: true, Ack: false, Push: false, Fin: false })
                    {
                        _estadoConexao = EstadoConexaoReceiver.SynRecebido;

                        _numeroAck = segmentoConfiavel.NumSeq + 1;

                        SegmentoConfiavel synAck = new SegmentoConfiavel(syn: true,
                                                                         ack: true,
                                                                         push: false,
                                                                         fin: false,
                                                                         numSeq: _numeroSeq,
                                                                         numAck: _numeroAck,
                                                                         data: Array.Empty<byte>(),
                                                                         checkSum: Array.Empty<byte>());

                        _threads.EnviarSegmento(synAck);

                        IniciarTemporizador();
                    }

                    break;
                }
                case EstadoConexaoReceiver.SynRecebido:
                {
                    if (segmentoConfiavel is { Syn: false, Ack: true, Push: false, Fin: false } && segmentoConfiavel.NumSeq == _numeroAck && segmentoConfiavel.NumAck == _numeroSeq + 1)
                    {
                        PararTemporizador();

                        _estadoConexao = EstadoConexaoReceiver.Estabelecida;

                        _numeroSeq = segmentoConfiavel.NumAck;
                    }

                    break;
                }
                case EstadoConexaoReceiver.Estabelecida:
                {
                    switch (segmentoConfiavel)
                    {
                        case { Syn: false, Ack: false, Push: true, Fin: false } when segmentoConfiavel.NumSeq == _numeroAck:
                        {
                            ResponderMensagem(segmentoConfiavel);

                            break;
                        }
                        case { Syn: false, Ack: false, Push: false, Fin: true } when segmentoConfiavel.NumSeq == _numeroAck:
                        {
                            _estadoConexao = EstadoConexaoReceiver.Fechando;

                            ResponderMensagem(segmentoConfiavel);

                            SegmentoConfiavel fin = new SegmentoConfiavel(syn: false,
                                                                          ack: false,
                                                                          push: false,
                                                                          fin: true,
                                                                          numSeq: _numeroSeq,
                                                                          numAck: _numeroAck,
                                                                          data: Array.Empty<byte>(),
                                                                          checkSum: Array.Empty<byte>());

                            _threads.EnviarSegmento(fin);

                            IniciarTemporizador();

                            break;
                        }
                    }

                    break;
                }
                case EstadoConexaoReceiver.Fechando:
                {
                    if (segmentoConfiavel is { Syn: false, Ack: true, Push: false, Fin: false } && segmentoConfiavel.NumSeq == _numeroAck && segmentoConfiavel.NumAck == _numeroSeq + 1)
                    {
                        PararTemporizador();

                        _estadoConexao = EstadoConexaoReceiver.Fechada;

                        _numeroSeq = segmentoConfiavel.NumAck;

                        _conexaoAtiva = false;
                    }

                    break;
                }
                case EstadoConexaoReceiver.Fechada: break;
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

        _threads.EnviarSegmento(ack);
    }

    private static void IniciarTemporizador()
    {
        _temporizadorRecebimento = new Timer(_timeoutMilissegundos);
        _temporizadorRecebimento.Elapsed += TemporizadorEncerrado;
        _temporizadorRecebimento.AutoReset = false;
        _temporizadorRecebimento.Start();
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
                case EstadoConexaoReceiver.Escuta:
                    break;
                case EstadoConexaoReceiver.SynRecebido:
                {
                    _estadoConexao = EstadoConexaoReceiver.Escuta;

                    break;
                }
                case EstadoConexaoReceiver.Estabelecida:
                    break;
                case EstadoConexaoReceiver.Fechando:
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

                    IniciarTemporizador();

                    break;
                }
                case EstadoConexaoReceiver.Fechada: break;
                default: throw new ArgumentOutOfRangeException();
            }
        }
    }
}
