using System.Net;
using System.Net.Sockets;
using System.Text;
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

    #region Consolidação

    private uint _totalMensagensEnviadas;
    private uint _totalMensagensRecebidas;
    private uint _totalMensagensEliminadas;
    private uint _totalMensagensAtrasadas;
    private uint _totalMensagensDuplicadas;
    private uint _totalMensagensCorrompidas;
    private uint _totalMensagensCortadas;

    #endregion

    #region Criação Canal

    public Canal(IPEndPoint pontoConexaoLocal, IPEndPoint pontoConexaoRemoto) : this(pontoConexaoLocal)
    {
        _pontoConexaoRemoto = pontoConexaoRemoto;
    }

    public Canal(IPEndPoint pontoConexaoLocal)
    {
        CarregarConfigs();

        _pontoConexaoLocal = pontoConexaoLocal;

        _socket.Client.Bind(_pontoConexaoLocal);
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

    #endregion

    #region Envio e Recebimento

    private byte[] SegmentoConfiavelParaByteArray(SegmentoConfiavel segmentoConfiavel)
    {
        return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(segmentoConfiavel));
    }

    private SegmentoConfiavel? ByteArrayParaSegmentoConfiavel(byte[] byteArray)
    {
        return JsonSerializer.Deserialize<SegmentoConfiavel>(Encoding.UTF8.GetString(byteArray));
    }

    public void EnviarSegmento(SegmentoConfiavel segmentoConfiavel)
    {
        try
        {
            byte[] bytesSegmentoConfiavel = SegmentoConfiavelParaByteArray(segmentoConfiavel);

            _socket.SendAsync(SegmentoConfiavelParaByteArray(segmentoConfiavel), _pontoConexaoRemoto);

            lock (_locker)
            {
                _totalMensagensEnviadas++;
            }
        }
        catch (SocketException socketException) { }
    }

    public SegmentoConfiavel? ReceberSegmento(CancellationToken tokenCancelamento)
    {
        try
        {
            ValueTask<UdpReceiveResult> taskRecebimento = _socket.ReceiveAsync(tokenCancelamento);

            byte[] segmentoRecebido = taskRecebimento.Result.Buffer;

            _pontoConexaoRemoto ??= taskRecebimento.Result.RemoteEndPoint;

            SegmentoConfiavel? segmentoConfiavelRecebido = ByteArrayParaSegmentoConfiavel(segmentoRecebido);

            lock (_locker)
            {
                if (segmentoConfiavelRecebido != null)
                {
                    _totalMensagensRecebidas++;
                }
            }

            return segmentoConfiavelRecebido;
        }
        catch
        {
            return null;
        }
    }

    //public bool ProcessarMensagem(SegmentoConfiavel segmento)
    //{


    //    lock (_locker)
    //    {
    //        byte[] mensagemModificada = mensagemRecebida.ToArray();

    //        bool mensagemEliminada = AplicarPropriedades(ref mensagemModificada);

    //        return !mensagemEliminada;
    //    }
    //}

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

