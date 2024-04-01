using System.Net.Sockets;
using System.Net;

namespace EP2;

public class Threads
{
    private Canal _canal;
    private CancellationTokenSource _tockenCancelamentoRecebimento = new CancellationTokenSource();

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

    public void Fechar()
    {
        _tockenCancelamentoRecebimento.Dispose();

        _canal.Fechar();
    }
}