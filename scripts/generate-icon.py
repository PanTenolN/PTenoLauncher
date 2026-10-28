"""Generate the original PTenoLauncher P icon using only Python's standard library."""
import struct, zlib
from pathlib import Path

def png(size):
    rows=[]
    for y in range(size):
        row=bytearray([0])
        for x in range(size):
            u=x*256/size; v=y*256/size
            color=(16,20,29,255)
            if 20<u<236 and 20<v<236: color=(30,53,48,255)
            if (64<u<102 and 66<v<204) or (98<u<176 and 66<v<104) or (98<u<176 and 124<v<160) or (156<u<194 and 94<v<134):
                color=(142,240,189,255)
            row.extend(color)
        rows.append(row)
    def chunk(t,b):
        return struct.pack(">I",len(b))+t+b+struct.pack(">I",zlib.crc32(t+b)&0xffffffff)
    return b"\x89PNG\r\n\x1a\n"+chunk(b"IHDR",struct.pack(">IIBBBBB",size,size,8,6,0,0,0))+chunk(b"IDAT",zlib.compress(b"".join(rows)))+chunk(b"IEND",b"")
sizes=[16,32,48,64,128,256]; blobs=[png(s) for s in sizes]; offset=6+16*len(sizes)
result=struct.pack("<HHH",0,1,len(sizes))
for size,blob in zip(sizes,blobs):
    result+=struct.pack("<BBBBHHII",size%256,size%256,0,0,1,32,len(blob),offset)
    offset+=len(blob)
target=Path(__file__).resolve().parents[1]/"src/PTenoLauncher.Desktop/Assets/launcher.ico"
target.parent.mkdir(parents=True,exist_ok=True)
target.write_bytes(result+b"".join(blobs))
print(target)
