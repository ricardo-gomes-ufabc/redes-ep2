namespace EP2;

//public record SegmentoConfiavel()
//{
//    public bool Syn;
//    public bool Ack;
//    public bool Fin;
//    public uint SeqNum;
//    public uint AckNum;
//    public uint WindowSize;
//    public byte[] Data;
//}

public record SegmentoConfiavel(bool Syn, bool Ack, bool Fin, uint SeqNum, byte[] Data, byte[] CheckSum);