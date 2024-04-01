using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;


namespace EP2;

internal class Canal
{
    private readonly Random _aleatorio = new Random();
    private readonly object _trava = new object();

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

    public void EnviarSegmento(byte[]? bytesSegmentoConfiavel)
    {
        try
        {
            lock (_trava)
            {
                _totalMensagensEnviadas++;
            }

            if (bytesSegmentoConfiavel != null)
            {
                _socket.SendAsync(bytesSegmentoConfiavel, _pontoConexaoRemoto);
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

            lock (_trava)
            {
                _totalMensagensRecebidas++;
            }

            _pontoConexaoRemoto ??= taskRecebimento.Result.RemoteEndPoint;

            SegmentoConfiavel? segmentoConfiavelRecebido = ByteArrayParaSegmentoConfiavel(segmentoRecebido);

            byte[]? checkSumRecebimento = GerarCheckSum(segmentoConfiavelRecebido);

            if (!checkSumRecebimento.SequenceEqual(segmentoConfiavelRecebido.CheckSum))
            {
                Console.WriteLine("Mensagem corrompida recebida descartada, checksum diferente");

                return null;
            }

            return segmentoConfiavelRecebido;

        }
        catch (JsonException)
        {
            Console.WriteLine("Mensagem corrompida recebida descartada");

            return null;
        }
        catch
        {
            return null;
        }
    }

    #endregion

    #region Aplicação de Propiedades

    public void ProcessarMensagem(SegmentoConfiavel segmentoConfiavel)
    {
        uint id = segmentoConfiavel.Ack ? segmentoConfiavel.NumAck : segmentoConfiavel.NumSeq;
        string tipoMensagem = "";

        if (segmentoConfiavel is { Syn: true, Ack: false })
        {
            tipoMensagem = "SYN";
        }
        else if (segmentoConfiavel is { Ack: true, Syn: false })
        {
            tipoMensagem = "ACK";
        }
        else if (segmentoConfiavel.Push)
        {
            tipoMensagem = "PUSH";
        }
        else if (segmentoConfiavel.Fin)
        {
            tipoMensagem = "FIN";
        }
        else
        {
            tipoMensagem = "SYNACK";
        }

        if (DeveriaAplicarPropriedade(_probabilidadeEliminacao))
        {
            _totalMensagensEliminadas++;

            EnviarSegmento(null);

            Console.WriteLine($"Mensagem {tipoMensagem} id {id} eliminada.");

            return;
        }

        segmentoConfiavel.SetCheckSum(GerarCheckSum(segmentoConfiavel));

        if (DeveriaAplicarPropriedade(_probabilidadeDuplicacao))
        {
            _totalMensagensDuplicadas++;

            byte[] bytesSegmentoDuplicado = SegmentoConfiavelParaByteArray(segmentoConfiavel);

            EnviarSegmento(bytesSegmentoDuplicado);

            Console.WriteLine($"Mensagem {tipoMensagem} id {id} duplicada.");
        }


        byte[] bytesSegmento = SegmentoConfiavelParaByteArray(segmentoConfiavel);

        if (DeveriaAplicarPropriedade(_probabilidadeCorrupcao))
        {
            CorromperSegmento(ref bytesSegmento);
            _totalMensagensCorrompidas++;
            Console.WriteLine($"Mensagem {tipoMensagem} id {id} corrompida.");
        }

        if (bytesSegmento.Length > _tamanhoMaximoBytes)
        {
            CortarSegmento(ref bytesSegmento);
            _totalMensagensCortadas++;
            Console.WriteLine($"Mensagem {tipoMensagem} id {id} cortada.");
        }

        if (_delayMilissegundos != 0)
        {
            Thread.Sleep(_delayMilissegundos);
            _totalMensagensAtrasadas++;
            Console.WriteLine($"Mensagem {tipoMensagem} id {id} atrasada.");
        }

        EnviarSegmento(bytesSegmento);
    }

    private byte[] GerarCheckSum(SegmentoConfiavel segmentoConfiavel)
    {
        var conteudoSegmento = new
        {
            Syn = segmentoConfiavel.Syn,
            Ack = segmentoConfiavel.Ack,
            Push = segmentoConfiavel.Push,
            Fin = segmentoConfiavel.Fin,
            NumSeq = segmentoConfiavel.NumSeq,
            NumAck = segmentoConfiavel.NumAck,
            Data = segmentoConfiavel.Data,
        };

        string jsonConteudo = JsonSerializer.Serialize(conteudoSegmento);

        return GetHash(jsonConteudo);
    }

    public static byte[] GetHash(string inputString)
    {
        using (HashAlgorithm algorithm = SHA256.Create())
        {
            return algorithm.ComputeHash(Encoding.UTF8.GetBytes(inputString));
        }
    }

    private bool DeveriaAplicarPropriedade(int probabilidade)
    {
        return _aleatorio.Next(minValue: 1, maxValue: 101) <= probabilidade;
    }

    private void CorromperSegmento(ref byte[] segmento)
    {
        int indice = _aleatorio.Next(minValue: 0, maxValue: segmento.Length);

        segmento[indice] = (byte)(~segmento[indice]); ;
    }

    private void CortarSegmento(ref byte[] segmento)
    {
        Array.Resize(ref segmento, _tamanhoMaximoBytes);
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

