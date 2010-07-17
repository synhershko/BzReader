#include <stdio.h>
#include "bzlib_private.h"

/* This program records bit locations in the file to be recovered.
   That means that if 64-bit ints are not supported, we will not
   be able to recover .bz2 files over 512MB (2^32 bits) long.
   On GNU supported platforms, we take advantage of the 64-bit
   int support to circumvent this problem.  Ditto MSVC.

   This change occurred in version 1.0.2; all prior versions have
   the 512MB limitation.
*/

#ifdef __GNUC__
   typedef  unsigned long long int  MaybeUInt64;
#  define MaybeUInt64_FMT "%Lu"
#else
#ifdef _MSC_VER
   typedef  unsigned __int64  MaybeUInt64;
#  define MaybeUInt64_FMT "%I64u"
#else
   typedef  unsigned int   MaybeUInt64;
#  define MaybeUInt64_FMT "%u"
#endif
#endif

typedef  unsigned int   UInt32;
typedef  int            Int32;
typedef  unsigned char  UChar;
typedef  char           Char;
typedef  unsigned char  Bool;
#define True    ((Bool)1)
#define False   ((Bool)0)

MaybeUInt64 bytesOut = 0;
MaybeUInt64 buffLive = 0;
UChar		buffer = 0;

/*---------------------------------------------------*/
/*--- Header bytes                                ---*/
/*---------------------------------------------------*/

#define BZ_HDR_B 0x42                         /* 'B' */
#define BZ_HDR_Z 0x5a                         /* 'Z' */
#define BZ_HDR_h 0x68                         /* 'h' */
#define BZ_HDR_0 0x30                         /* '0' */
 
/*---------------------------------------------------*/
/*--- I/O errors                                  ---*/
/*---------------------------------------------------*/

/*---------------------------------------------*/
static void readError ( void )
{
   exit(1);
}

/*---------------------------------------------*/
static void writeError ( void )
{
   exit(1);
}

/*---------------------------------------------*/
static void mallocFail ( Int32 n )
{
   exit(1);
}

/*---------------------------------------------------*/
/*--- Bit stream I/O                              ---*/
/*---------------------------------------------------*/

typedef
   struct {
      FILE*  handle;
      Int32  buffer;
      Int32  buffLive;
      Char   mode;
   }
   BitStream;


/*---------------------------------------------*/
static BitStream* bsOpenReadStream ( FILE* stream )
{
   BitStream *bs = malloc ( sizeof(BitStream) );
   if (bs == NULL) mallocFail ( sizeof(BitStream) );
   bs->handle = stream;
   bs->buffer = 0;
   bs->buffLive = 0;
   bs->mode = 'r';
   return bs;
}

/*---------------------------------------------*/
/*--
   Returns 0 or 1, or 2 to indicate EOF.
--*/
static Int32 bsGetBit ( BitStream* bs )
{
   if (bs->buffLive > 0) {
      bs->buffLive --;
      return ( ((bs->buffer) >> (bs->buffLive)) & 0x1 );
   } else {
      Int32 retVal = getc ( bs->handle );
      if ( retVal == EOF ) {
         if (errno != 0) readError();
         return 2;
      }
      bs->buffLive = 7;
      bs->buffer = retVal;
      return ( ((bs->buffer) >> 7) & 0x1 );
   }
}

/*---------------------------------------------*/
static void bsClose ( BitStream* bs )
{
   Int32 retVal;

   retVal = fclose ( bs->handle );

   if (retVal == EOF)
   {
      if (bs->mode == 'w')
	  {
		  writeError();
	  }
	  else
	  {
		  readError();
	  }
   }

   free(bs);
}

/*---------------------------------------------*/
static void bufPutBit(char* bs, Int32 bit)
{
	if (buffLive == 8)
	{
		bs[bytesOut] = buffer;
		bytesOut++;
		buffLive = 1;
		buffer = bit & 0x1;
	}
	else
	{
		buffer = (buffer << 1) | (bit & 0x1);
		buffLive++;
	};
}

/*---------------------------------------------*/
static void bufPutUChar(char* buf, UChar c)
{
   Int32 i;

   for (i = 7; i >= 0; i--)
   {
      bufPutBit(buf, (((UInt32) c) >> i) & 0x1);
   }
}

/*---------------------------------------------*/
static void bufPutUInt32(char* buf, UInt32 c)
{
   Int32 i;

   for (i = 31; i >= 0; i--)
   {
      bufPutBit(buf, (c >> i) & 0x1);
   }
}

/*---------------------------------------------------*/
/*---                                             ---*/
/*---------------------------------------------------*/

#define BLOCK_HEADER_HI  0x00003141UL
#define BLOCK_HEADER_LO  0x59265359UL

#define BLOCK_ENDMARK_HI 0x00001772UL
#define BLOCK_ENDMARK_LO 0x45385090UL

/* Increase if necessary.  However, a .bz2 file with > 50000 blocks
   would have an uncompressed size of at least 40GB, so the chances
   are low you'll need to up this.
*/

#define BZ_MAX_HANDLED_BLOCKS 200000

MaybeUInt64 bStart [BZ_MAX_HANDLED_BLOCKS];
MaybeUInt64 bEnd   [BZ_MAX_HANDLED_BLOCKS];
MaybeUInt64 rbStart[BZ_MAX_HANDLED_BLOCKS];
MaybeUInt64 rbEnd  [BZ_MAX_HANDLED_BLOCKS];

int BZ_API(BZ2_bzLocateBlocks) ( 
								const char*	path,
								long long*	beginnings,
								long long*	ends,
								long long*	bufSize,
								int*  blocks_pct_done
								)
{
	Int32       b, currBlock, rbCtr;
	MaybeUInt64 bitsRead;
	BitStream*	bsIn;

	UInt32      buffHi, buffLo;

	long long originalBufSize = *bufSize;
	long long total_bz2_size;

	FILE *inFile = fopen(path, "rb");
	
	if (inFile == NULL)
	{
		return BZ_IO_ERROR;
	}
	_fseeki64(inFile, 0, SEEK_END);
	total_bz2_size = _ftelli64(inFile);
	_fseeki64(inFile, 0, SEEK_SET);

	bsIn = bsOpenReadStream(inFile);

	bitsRead = 0;
	buffHi = buffLo = 0;
	currBlock = 0;
	bStart[currBlock] = 0;
	*blocks_pct_done = 0;
	rbCtr = 0;

	while (True)
	{
		b = bsGetBit(bsIn);
		
		bitsRead++;
		
		if (b == 2)
		{
			if (bitsRead >= bStart[currBlock] &&
				(bitsRead - bStart[currBlock]) >= 40)
			{
				bEnd[currBlock] = bitsRead-1;
			}
			else
			{
				currBlock--;
			}

			break;
		}

		buffHi = (buffHi << 1) | (buffLo >> 31);
		buffLo = (buffLo << 1) | (b & 1);

		if ( ( (buffHi & 0x0000ffff) == BLOCK_HEADER_HI 
			&& buffLo == BLOCK_HEADER_LO)
			|| 
			( (buffHi & 0x0000ffff) == BLOCK_ENDMARK_HI 
			&& buffLo == BLOCK_ENDMARK_LO)
			) {
				if (bitsRead > 49) {
					bEnd[currBlock] = bitsRead-49;
				} else {
					bEnd[currBlock] = 0;
				}

				if (currBlock > 0 &&
					(bEnd[currBlock] - bStart[currBlock]) >= 130)
				{
					rbStart[rbCtr] = bStart[currBlock];
					rbEnd[rbCtr] = bEnd[currBlock];
					rbCtr++;
				}

				if (currBlock >= BZ_MAX_HANDLED_BLOCKS ||
					currBlock >= originalBufSize)
				{
					return BZ_OUTBUFF_FULL;
				}

				currBlock++;

				if (total_bz2_size > 0)
				{
					*blocks_pct_done = (int)((double)_ftelli64(inFile) / (double)total_bz2_size * 100); // report progress
				}
				
				bStart[currBlock] = bitsRead;
		}
	}

	bsClose ( bsIn );

	/*-- identified blocks run from 1 to rbCtr inclusive. --*/

	if (rbCtr < 1)
	{
		return BZ_DATA_ERROR;
	}

	*bufSize = rbCtr;

	memcpy(beginnings, rbStart, rbCtr * sizeof(MaybeUInt64));
	memcpy(ends, rbEnd, rbCtr * sizeof(MaybeUInt64));

	return BZ_OK;
}

int BZ_API(BZ2_bzLoadBlock) (
							   const char*	path,
							   long long	beginning,
							   long long	end,
							   char*		buf,
							   long long*	bufSize
							   )
{
	FILE*		inFile;
	BitStream*	bsIn;
	MaybeUInt64 bitsRead;
	Int32       i, b;

	UInt32      buffHi, buffLo, blockCRC;

	// beginning and end are bit offsets, 2 bytes for the starting and ending bytes padding, 22 bytes various headers

	if (((end - beginning) / 8 + 2 + 22) >= *bufSize)
	{
		return BZ_OUTBUFF_FULL;
	}

	inFile = fopen(path, "rb");

	if (inFile == NULL)
	{
		return BZ_IO_ERROR;
	}

	bsIn = bsOpenReadStream(inFile);

	// Seek to the specified bit offset

	if (_fseeki64(inFile, beginning >> 3, SEEK_SET))
	{
		bsClose(bsIn);

		return BZ_IO_ERROR;
	}

	for (i = 0; i < (beginning & 7); i++)
	{
		bsGetBit(bsIn);
	}

	bytesOut = 0;
	buffLive = 0;
	buffer = 0;

	// Write bzip2 header to buffer

	bufPutUChar(buf, BZ_HDR_B);    
	bufPutUChar(buf, BZ_HDR_Z);    
	bufPutUChar(buf, BZ_HDR_h);    
	bufPutUChar(buf, BZ_HDR_0 + 9);
	bufPutUChar(buf, 0x31);
	bufPutUChar(buf, 0x41);
	bufPutUChar(buf, 0x59);
	bufPutUChar(buf, 0x26);
	bufPutUChar(buf, 0x53);
	bufPutUChar(buf, 0x59);

	blockCRC = 0;
	bitsRead = beginning;
	buffHi = buffLo = 0;

	b = 0;

	while (True)
	{
		b = bsGetBit(bsIn);

		if (b == 2) break;

		buffHi = (buffHi << 1) | (buffLo >> 31);
		buffLo = (buffLo << 1) | (b & 1);
		
		if (bitsRead == 47 + beginning) 
		{
			blockCRC = (buffHi << 16) | (buffLo >> 16);
		}

		if (bitsRead <= end)
		{
			bufPutBit(buf, b);
		}

		bitsRead++;

		if (bitsRead == end + 1)
		{
			bufPutUChar(buf, 0x17);
			bufPutUChar(buf, 0x72);
			bufPutUChar(buf, 0x45);
			bufPutUChar(buf, 0x38);
			bufPutUChar(buf, 0x50);
			bufPutUChar(buf, 0x90);
			bufPutUInt32(buf, blockCRC);

			break;
		}
	}

	while (buffLive < 8 &&
		buffLive > 0)
	{
		buffLive++;
		buffer <<= 1;
	}

	if (buffLive > 0)
	{
		buf[bytesOut] = buffer;
		bytesOut++;
	}

	bsClose(bsIn);

	*bufSize = bytesOut;

	return BZ_OK;
}

/*-----------------------------------------------------------*/
/*--- end                                  bzip2recover.c ---*/
/*-----------------------------------------------------------*/
