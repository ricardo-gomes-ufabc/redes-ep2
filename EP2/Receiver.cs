using System;
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
    private static Canal _canal;
    private static bool _conexaoAtiva;

    private static EstadoConexaoReceiver _estadoConexao = EstadoConexaoReceiver.Fechada;

    private static uint _numeroSeq = 0;
    private static uint _numeroAck = 0;

    private static int _timeoutMilissegundos = 30000;
    private static Timer _temporizadorRecebimento;

    private static object _trava = new object();

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
        _estadoConexao = EstadoConexaoReceiver.Escuta;

        while (_conexaoAtiva)
        {
            SegmentoConfiavel? segmentoConfiavel = _canal.ReceberSegmento();

            TratarMensagem(segmentoConfiavel);
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

                        SegmentoConfiavel synAck = new SegmentoConfiavel(Syn: true,
                                                                         Ack: true,
                                                                         Push: false,
                                                                         Fin: false,
                                                                         NumSeq: _numeroSeq,
                                                                         NumAck: _numeroAck,
                                                                         Data: new byte[] { },
                                                                         CheckSum: new byte[] { });

                        _canal.EnviarSegmento(synAck);

                        _temporizadorRecebimento = new Timer(_timeoutMilissegundos);
                        _temporizadorRecebimento.Elapsed += TemporizadorEncerrado;
                        _temporizadorRecebimento.AutoReset = false;
                    }

                    break;
                }
                case EstadoConexaoReceiver.SynRecebido:
                {
                    if (segmentoConfiavel is { Syn: false, Ack: true, Push: false, Fin: false })
                    {
                        _temporizadorRecebimento.Stop();
                        _temporizadorRecebimento.Dispose();

                        _estadoConexao = EstadoConexaoReceiver.Estabelecida;

                        _numeroSeq = segmentoConfiavel.NumAck + 1;
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
                        case { Syn: false, Ack: false, Push: false, Fin: true }:
                        {
                            _estadoConexao = EstadoConexaoReceiver.Fechando;

                            ResponderMensagem(segmentoConfiavel);

                            SegmentoConfiavel fin = new SegmentoConfiavel(Syn: false,
                                                                          Ack: false,
                                                                          Push: false,
                                                                          Fin: true,
                                                                          NumSeq: _numeroSeq,
                                                                          NumAck: _numeroAck,
                                                                          Data: new byte[] { },
                                                                          CheckSum: new byte[] { });

                            _canal.EnviarSegmento(fin);

                            _temporizadorRecebimento = new Timer(_timeoutMilissegundos);
                            _temporizadorRecebimento.Elapsed += TemporizadorEncerrado;
                            _temporizadorRecebimento.AutoReset = false;

                            break;
                        }
                    }

                    break;
                }
                case EstadoConexaoReceiver.Fechando:
                {
                    if (segmentoConfiavel is { Syn: false, Ack: true, Push: false, Fin: false })
                    {
                        _temporizadorRecebimento.Stop();
                        _temporizadorRecebimento.Dispose();

                        _estadoConexao = EstadoConexaoReceiver.Fechada;

                        _conexaoAtiva = false;
                    }

                    break;
                }
                default:
                {
                    throw new ArgumentOutOfRangeException();
                }
            }
        }
    }

    private static void ResponderMensagem(SegmentoConfiavel segmentoConfiavel)
    {
        _numeroAck = segmentoConfiavel.NumSeq + 1;

        SegmentoConfiavel ack = new SegmentoConfiavel(Syn: false,
                                                      Ack: true,
                                                      Push: false,
                                                      Fin: false,
                                                      NumSeq: _numeroSeq,
                                                      NumAck: _numeroAck,
                                                      Data: new byte[] { },
                                                      CheckSum: new byte[] { });

        _canal.EnviarSegmento(ack);
    }

    private static void TemporizadorEncerrado(object? state, ElapsedEventArgs elapsedEventArgs)
    {
        lock (_trava)
        {
            _temporizadorRecebimento.Stop();
            _temporizadorRecebimento.Dispose();

            _estadoConexao = EstadoConexaoReceiver.Escuta;
        }
    }
}
