using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace EP2;

internal class Canal
{
    private readonly Random _aleatorio = new Random();
    private readonly object _locker = new object();

    #region Socket

    private IPEndPoint _pontoConexaoLocal;
    private IPEndPoint? _pontoConexaoRemoto;
    public readonly UdpClient _socket = new UdpClient();

    #endregion

    #region Configs

    private int _probabilidadeEliminacao;
    private int _delayMilissegundos;
    private int _probabilidadeDuplicacao;
    private int _probabilidadeCorrupcao;
    private int _tamanhoMaximoBytes;

    #endregion

    #region Config Extra

    private bool _modoServidor;

    #endregion

    #region Consolidação

    private uint _totalMensagensEnviadas;
    private uint _totalMensagensRecebidas;
    private uint _totalMensagensEliminadas;
    private uint _totalMensagensAtrasadas;
    private uint _totalMensagensDuplicadas;
    private uint _totalMensagensCorrompidas;
    private uint _totalMensagensCortadas;

    #endregion

    public Canal(IPEndPoint pontoConexaoLocal, IPEndPoint pontoConexaoRemoto, bool modoServidor) : this(pontoConexaoLocal, modoServidor)
    {
        _pontoConexaoRemoto = pontoConexaoRemoto;
    }

    public Canal(IPEndPoint pontoConexaoLocal, bool modoServidor)
    {
        CarregarConfigs();

        _pontoConexaoLocal = pontoConexaoLocal;
        _modoServidor = modoServidor;

        _socket.Client.Bind(_pontoConexaoLocal);

        if (!_modoServidor)
        {
            _socket.Client.ReceiveTimeout = 3000;
        }
    }

    private void CarregarConfigs()
    {
        string json = File.ReadAllText(path: $@"{AppContext.BaseDirectory}/appsettings.json");

        using (JsonDocument document = JsonDocument.Parse(json))
        {
            JsonElement root = document.RootElement;

            int porcentagemTaxaEliminacao = root.GetProperty("ProbabilidadeEliminacao").GetInt32();
            int delayMilissegundos = root.GetProperty("DelayMilissegundos").GetInt32();
            int porcentagemTaxaDuplicacao = root.GetProperty("ProbabilidadeDuplicacao").GetInt32();
            int porcentagemTaxaCorrupcao = root.GetProperty("ProbabilidadeCorrupcao").GetInt32();
            int tamanhoMaximoBytes = root.GetProperty("TamanhoMaximoBytes").GetInt32();

            _probabilidadeEliminacao = porcentagemTaxaEliminacao;
            _delayMilissegundos = delayMilissegundos;
            _probabilidadeDuplicacao = porcentagemTaxaDuplicacao;
            _probabilidadeCorrupcao = porcentagemTaxaCorrupcao;
            _tamanhoMaximoBytes = tamanhoMaximoBytes;
        }
    }

    #region Criação UDP

    public byte[] GerarMensagemUdp()
    {
        byte[] segmento = new byte[_aleatorio.Next(minValue: 1, maxValue: 1024)];

        _aleatorio.NextBytes(segmento);

        return segmento;
    }

    #endregion

    #region Envio e Recebimento

    public void EnviarMensagem(byte[] mensagem)
    {
        _socket.Send(mensagem, _pontoConexaoRemoto);

        lock (_locker)
        {
            _totalMensagensEnviadas++;
        }
        
        Console.WriteLine("Mensagem UDP enviada");
    }

    public byte[]? ReceberMensagem()
    {
        try
        {
            return _socket.Receive(ref _pontoConexaoRemoto);
        }
        catch
        {
            return null;
        }
    }

    public bool ProcessarMensagem(byte[]? mensagemRecebida)
    {
        lock (_locker)
        {
            if (mensagemRecebida == null)
            {
                return false;
            }

            _totalMensagensRecebidas++;

            byte[] mensagemModificada = mensagemRecebida.ToArray();

            bool mensagemEliminada = AplicarPropriedades(ref mensagemModificada);

            ValidarSegmento(original: mensagemRecebida, modificado: mensagemModificada);

            return !mensagemEliminada;
        }
    }

    private void ValidarSegmento(byte[] original, byte[] modificado)
    {
        if (original.Length != modificado.Length)
        {
            _totalMensagensCortadas++;
        }
        else if(!original.SequenceEqual(modificado))
        {
            _totalMensagensCorrompidas++;
        }
    }

    #endregion

    #region Aplicação de Propiedades

    private bool AplicarPropriedades(ref byte[] mensagem)
    {
        if (DeveriaAplicarPropriedade(_probabilidadeEliminacao))
        {
            _totalMensagensEliminadas++;
            _totalMensagensRecebidas--;
            Console.WriteLine("Mensagem eliminada.");
            return true;
        }

        if (_delayMilissegundos != 0)
        {
            Thread.Sleep(_delayMilissegundos);
            _totalMensagensAtrasadas++;
            Console.WriteLine("Mensagem atrasada.");
        }

        if (DeveriaAplicarPropriedade(_probabilidadeDuplicacao))
        {
            _totalMensagensDuplicadas++;
            _totalMensagensRecebidas++;
            Console.WriteLine("Mensagem duplicada.");

            if (_modoServidor)
            {
                EnviarMensagem(GerarMensagemUdp());
            }
        }

        if (DeveriaAplicarPropriedade(_probabilidadeCorrupcao))
        {
            CorromperSegmento(ref mensagem);
            Console.WriteLine("Mensagem corrompida.");
        }

        CortarSegmento(ref mensagem);

        return false;
    }

    private bool DeveriaAplicarPropriedade(int probabilidade)
    {
        return _aleatorio.Next(minValue: 0, maxValue: 101) <= probabilidade;
    }

    private void CorromperSegmento(ref byte[] segmento)
    {
        int indice = _aleatorio.Next(minValue: 0, maxValue: segmento.Length);

        segmento[indice] = (byte)(~segmento[indice]); ;
    }

    private void CortarSegmento(ref byte[] segmento)
    {
        if (segmento.Length > _tamanhoMaximoBytes)
        {
            Array.Resize(ref segmento, _tamanhoMaximoBytes);
            Console.WriteLine("Mensagem cortada.");
        }
    }

    #endregion

    #region Finalização

    public void Fechar()
    {
        ConsolidarResultados();

        _socket.Close();

        _socket.Dispose();
    }

    private void ConsolidarResultados()
    {
        Console.WriteLine(value: $"\n" +
                                 $"\nTotal de mensagens enviadas: {_totalMensagensEnviadas}" +
                                 $"\nTotal de mensagens recebidas: {_totalMensagensRecebidas}" +
                                 $"\nTotal de mensagens eliminadas: {_totalMensagensEliminadas}" +
                                 $"\nTotal de mensagens atrasadas: {_totalMensagensAtrasadas}" +
                                 $"\nTotal de mensagens duplicadas: {_totalMensagensDuplicadas}" +
                                 $"\nTotal de mensagens corrompidas: {_totalMensagensCorrompidas}" +
                                 $"\nTotal de mensagens cortadas: {_totalMensagensCortadas}");
    }

    #endregion


}

