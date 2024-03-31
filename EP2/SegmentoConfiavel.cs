namespace EP2;

public record SegmentoConfiavel(bool Syn, bool Ack, bool Push, bool Fin, uint NumSeq, uint NumAck, byte[] Data, byte[] CheckSum);