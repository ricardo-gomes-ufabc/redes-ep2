namespace EP2;

public class SegmentoConfiavel
{
    public bool Syn { get; private set; }
    public bool Ack { get; private set; }
    public bool Push { get; private set; }
    public bool Fin { get; private set; }
    public uint NumSeq { get; private set; }
    public uint NumAck { get; private set; }
    public byte[] Data { get; private set; }
    public byte[] CheckSum { get; private set; }

    public SegmentoConfiavel(bool syn, bool ack, bool push, bool fin, uint numSeq, uint numAck, byte[] data, byte[] checkSum)
    {
        Syn = syn;
        Ack = ack;
        Push = push;
        Fin = fin;
        NumSeq = numSeq;
        NumAck = numAck;
        Data = data;
        CheckSum = checkSum;
    }

    public void SetNumAck(uint numAck)
    {
        NumAck = numAck;
    }

    public void SetCheckSum(byte[] checkSum)
    {
        CheckSum = checkSum;
    }
}