using System.Net;
using System.Timers;
using Timer = System.Timers.Timer;


namespace EP2;

public class Threads
{
    private Canal _canal;

    private CancellationTokenSource _tockenCancelamentoRecebimento = new CancellationTokenSource();

    private static int _timeoutMilissegundos = 500;
    private static Timer _temporizadorRecebimento = new Timer(_timeoutMilissegundos);

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
        _canal.ProcessarMensagem(segmento);
    }

    public SegmentoConfiavel? ReceberSegmento()
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

    public void ConfigurarTemporizador(ElapsedEventHandler evento)
    {
        _temporizadorRecebimento.Elapsed += evento;
        _temporizadorRecebimento.AutoReset = true;
    }

    public void IniciarTemporizador()
    {
        _temporizadorRecebimento.Enabled = true;
    }

    public void PararTemporizador()
    {
        _temporizadorRecebimento.Stop();
    }

    public void Fechar()
    {
        _tockenCancelamentoRecebimento.Dispose();

        _temporizadorRecebimento.Dispose();

        _canal.Fechar();
    }
}