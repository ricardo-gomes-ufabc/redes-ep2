using System.Net;
using System.Timers;
using Timer = System.Timers.Timer;


namespace EP2;

public class Threads
{
    private Canal _canal;

    private CancellationTokenSource _tockenCancelamentoRecebimento = new CancellationTokenSource();

    private static int _timeoutMilissegundos = 15000;
    private static Timer _temporizadorRecebimento;

    public Threads(IPEndPoint pontoConexaoLocal, IPEndPoint pontoConexaoRemoto)
    {
        _canal = new Canal(pontoConexaoLocal, pontoConexaoRemoto);
    }

    public Threads(IPEndPoint pontoConexaoLocal)
    {
        _canal = new Canal(pontoConexaoLocal);
    }

    public void EnviarSegmento(SegmentoConfiavel segmento)
    {
        _canal.EnviarSegmento(segmento);
    }

    public void EnviarSegmentoAsync(SegmentoConfiavel segmento)
    {
        Task.Run(() => _canal.EnviarSegmento(segmento));
    }

    public SegmentoConfiavel ReceberSegmento()
    {
        SegmentoConfiavel? segmentoRecebido;

        segmentoRecebido = _canal.ReceberSegmento(_tockenCancelamentoRecebimento.Token);

        if (_tockenCancelamentoRecebimento.Token.IsCancellationRequested)
        {
            _tockenCancelamentoRecebimento = new CancellationTokenSource();
        }

        return segmentoRecebido;
    }

    public void CancelarRecebimento()
    {
        _tockenCancelamentoRecebimento.Cancel();
    }

    public void IniciarTemporizador(ElapsedEventHandler evento)
    {
        _temporizadorRecebimento = new Timer(_timeoutMilissegundos);
        _temporizadorRecebimento.Elapsed += evento;
        _temporizadorRecebimento.AutoReset = false;
        _temporizadorRecebimento.Start();
    }

    public void PararTemporizador()
    {
        _temporizadorRecebimento.Stop();
        _temporizadorRecebimento.Dispose();
    }

    public void Fechar()
    {
        _tockenCancelamentoRecebimento.Dispose();

        _canal.Fechar();
    }
}