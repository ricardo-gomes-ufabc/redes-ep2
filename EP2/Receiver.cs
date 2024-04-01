using System.Net;
using System.Timers;


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
    private static ElapsedEventHandler _evento = new ElapsedEventHandler(TemporizadorEncerrado);

    private static bool _conexaoAtiva;

    private static EstadoConexaoReceiver _estadoConexao = EstadoConexaoReceiver.Fechada;

    private static uint _numeroSeq = 0;
    private static uint _numeroAck = 0;

    private static CancellationTokenSource _tockenCancelamentoRecebimento = new CancellationTokenSource();

    private static object _trava = new object();

    private const int _numeroTentaivasFin = 3;
    private static int _contadorAckFinalNaoRecebido = 0;

    private static void Main()
    {
        try
        {
            Console.Write("Digite a porta do Receiver: ");

            int porta = Convert.ToInt32(Console.ReadLine());

            IPEndPoint pontoConexao = new IPEndPoint(IPAddress.Any, porta);

            _threads = new Threads(pontoConexaoLocal: pontoConexao);

            _threads.ConfigurarTemporizador(TemporizadorEncerrado);

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

                        _threads.IniciarTemporizador();
                    }

                    break;
                }
                case EstadoConexaoReceiver.SynRecebido:
                {
                    if (segmentoConfiavel is { Syn: false, Ack: true, Push: false, Fin: false } && segmentoConfiavel.NumSeq == _numeroAck && segmentoConfiavel.NumAck == _numeroSeq + 1)
                    {
                        _threads.PararTemporizador();

                        _estadoConexao = EstadoConexaoReceiver.Estabelecida;

                        _numeroSeq = segmentoConfiavel.NumAck;
                    }

                    break;
                }
                case EstadoConexaoReceiver.Estabelecida:
                {
                    switch (segmentoConfiavel)
                    {
                        case { Syn: false, Ack: false, Push: true, Fin: false }:
                        {
                            if (segmentoConfiavel.NumSeq == _numeroAck)
                            {
                                Console.WriteLine($"Mensagem id {segmentoConfiavel.NumSeq} recebida na ordem, entregando para a camada de aplicação.");

                                ResponderMensagem(segmentoConfiavel);
                            }
                            else if (segmentoConfiavel.NumSeq < _numeroAck)
                            {
                                Console.WriteLine($"Mensagem id {segmentoConfiavel.NumSeq} já recebida anteriormente.");

                                ResponderMensagemRecebidaAnteriormente(segmentoConfiavel);
                            }
                            else
                            {
                                string identificadores = String.Join(',', Enumerable.Range((int) _numeroAck, Convert.ToInt32(segmentoConfiavel.NumSeq - _numeroAck)));

                                Console.WriteLine($"Mensagem id {segmentoConfiavel.NumSeq} recebida fora de ordem, ainda não recebidos os identificadores {identificadores}.");
                            }

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

                            _threads.IniciarTemporizador();

                            break;
                        }
                    }

                    break;
                }
                case EstadoConexaoReceiver.Fechando:
                {
                    if (segmentoConfiavel is { Syn: false, Ack: true, Push: false, Fin: false } && segmentoConfiavel.NumSeq == _numeroAck && segmentoConfiavel.NumAck == _numeroSeq + 1)
                    {
                        _threads.PararTemporizador();

                        _estadoConexao = EstadoConexaoReceiver.Fechada;

                        _numeroSeq = segmentoConfiavel.NumAck;

                        _conexaoAtiva = false;
                    }
                    else if (segmentoConfiavel == null)
                    {
                        if (_contadorAckFinalNaoRecebido < _numeroTentaivasFin)
                        {
                            _contadorAckFinalNaoRecebido += 1;

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
                        }
                        else
                        {
                            _estadoConexao = EstadoConexaoReceiver.Fechada;

                            _conexaoAtiva = false;
                        }
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

    private static void ResponderMensagemRecebidaAnteriormente(SegmentoConfiavel segmentoConfiavel)
    {
        SegmentoConfiavel ack = new SegmentoConfiavel(syn: false,
                                                      ack: true,
                                                      push: false,
                                                      fin: false,
                                                      numSeq: _numeroSeq,
                                                      numAck: segmentoConfiavel.NumSeq + 1,
                                                      data: Array.Empty<byte>(),
                                                      checkSum: Array.Empty<byte>());

        _threads.EnviarSegmento(ack);
    }

    private static void TemporizadorEncerrado(object? state, ElapsedEventArgs elapsedEventArgs)
    {
        _threads.PararTemporizador();

        switch (_estadoConexao)
        {
            case EstadoConexaoReceiver.Escuta:
                break;
            case EstadoConexaoReceiver.SynRecebido:
            {
                lock (_trava)
                {
                    _estadoConexao = EstadoConexaoReceiver.Escuta;
                }

                break;
            }
            case EstadoConexaoReceiver.Estabelecida:
                break;
            case EstadoConexaoReceiver.Fechando:
            {
                _threads.CancelarRecebimento();

                break;
            }
            case EstadoConexaoReceiver.Fechada: break;
            default: throw new ArgumentOutOfRangeException();
        }
    }
}
