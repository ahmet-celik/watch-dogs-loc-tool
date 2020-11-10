using System;
using System.Collections.Generic;
using System.IO;
using Gibbed.IO;

namespace watch_dogs_loc
{
    public class Loc
    {
        short magic, version;
        short language;
        ushort table_length;
        uint tree_offset;
        Table[] table;

        uint tree_meta_offset;

        uint[] tree_meta;

        uint nodes_count;

        public void Read(Stream input)
        {
            magic = input.ReadValueS16();
            version = input.ReadValueS16();
            if (magic != 0x4C53 /*LS*/ || version != 1)
            {
                Console.WriteLine("Not a valid loc file!");
                Environment.Exit(1);
            }
            language = input.ReadValueS16();
            table_length = input.ReadValueU16();
            tree_offset = input.ReadValueU32();
            table = new Table[table_length];
            long end_of_tables = -1;
            for (ushort i = 0; i < table_length; ++i)
            {
                table[i] = new Table();
                end_of_tables = table[i].Read(input);
            }
            tree_meta_offset = (uint)(end_of_tables + (-end_of_tables & 3));
            uint tree_meta_length = (tree_offset - tree_meta_offset) >> 2;
            tree_meta = new uint[tree_meta_length];
            input.Position = tree_meta_offset;
            for (uint i = 0; i < tree_meta_length; ++i)
            {
                tree_meta[(tree_meta_length - i - 1)] = input.ReadValueU32();
            }
            nodes_count = input.ReadValueU32();
        }

        public void Export(Stream input, String filename)
        {
            using (StreamWriter text = new StreamWriter(filename + ".txt", false, System.Text.Encoding.Unicode))
            {
                foreach (Table t in table)
                {
                    foreach (SubTableIds ids in t.sub_table_ids)
                    {
                        foreach (List<Id> ids_64 in ids.ids)
                        {
                            foreach (Id id in ids_64)
                            {
                                if (id.is_pseudo)
                                {
                                    continue;
                                }

                                uint bit_length = id.hi - id.lo;
                                if (bit_length == 0)
                                {
                                    text.WriteLine(id.id + "=");
                                    id.str = "";
                                    continue;
                                }
                                List<UInt16> str_bytes = new List<UInt16>();
                                uint byte_offset = ids.start + (id.lo >> 3);
                                uint bit_left = (id.lo & 7);
                                input.Position = byte_offset;
                                uint current_uint = input.ReadValueU32(Endian.Big) << (byte)bit_left;
                                byte_offset += 4;
                                while ((Int32)bit_length > 0)
                                {
                                    uint current_uint_masked = current_uint & 0xFFFFFFE0;
                                    uint tree_position = FindTreePosition(input, ref bit_length, ref byte_offset, ref bit_left, ref current_uint, current_uint_masked);
                                    DepthFirstSearch(input, str_bytes, tree_position);
                                }
                                string line = ToString(str_bytes);
                                id.str = line;
                                text.WriteLine(id.id + "=" + line.Replace("\r", "[CR]").Replace("\n", "[LF]"));
                            }
                        }
                    }
                }
            }
        }

        private void DepthFirstSearch(Stream input, List<ushort> str_bytes, uint tree_position)
        {
            Stack<UInt16> stack = new Stack<UInt16>();
            stack.Push((UInt16)tree_position);
            while (stack.Count > 0)
            {
                UInt16 idx = stack.Pop();
                input.Position = tree_offset + 4 * idx;
                UInt32 tree_node = input.ReadValueU32();
                if (tree_node <= 0xFFFF)
                {
                    str_bytes.Add((UInt16)tree_node);
                }
                else
                {
                    stack.Push((UInt16)tree_node);
                    stack.Push((UInt16)(tree_node >> 16));
                }
            }
        }

        private uint FindTreePosition(Stream input, ref uint bit_length, ref uint byte_offset, ref uint bit_left, ref uint current_uint, uint current_uint_masked)
        {
            uint tree_position;
            bool was_24 = false;
            if (current_uint_masked >= tree_meta[6])
            {
                if (current_uint_masked >= tree_meta[8])
                {
                    if (current_uint_masked >= tree_meta[10])
                    {
                        Environment.Exit(42);
                    }
                    was_24 = true;
                    bit_length -= 24;
                    tree_position = tree_meta[11] + current_uint_masked >> 8;
                    //// fixing
                    bit_left += 16;
                    input.Position = byte_offset;
                    uint next_byte = input.ReadValueU8();
                    byte_offset += 1;
                    current_uint = (current_uint << 24) + (next_byte << (byte)bit_left);
                    if (bit_left >= 16)
                    {
                        bit_left -= 8;
                        next_byte = input.ReadValueU8();
                        byte_offset += 1;
                        current_uint += (next_byte << (byte)bit_left);
                    }
                }
                else
                {
                    // consume 16 bits
                    current_uint <<= 16;
                    bit_length -= 16;
                    tree_position = tree_meta[9] + current_uint_masked >> 16;
                    bit_left += 8;
                }
            }
            else
            {
                if (current_uint_masked >= tree_meta[2])
                {
                    if (current_uint_masked >= tree_meta[4])
                    {
                        // consume 14 bits
                        current_uint <<= 14;
                        bit_length -= 14;
                        tree_position = tree_meta[7] + current_uint_masked >> 18;
                        bit_left += 6;

                    }
                    else
                    {
                        // consume 12 bits
                        current_uint <<= 12;
                        bit_length -= 12;
                        tree_position = tree_meta[5] + current_uint_masked >> 20;
                        bit_left += 4;
                    }
                }
                else
                {
                    if (current_uint_masked >= tree_meta[0])
                    {
                        // consume 10 bits
                        current_uint <<= 10;
                        bit_length -= 10;
                        tree_position = tree_meta[3] + current_uint_masked >> 22;
                        bit_left += 2;

                    }
                    else
                    {
                        // consume 8 bits
                        current_uint <<= 8;
                        bit_length -= 8;
                        tree_position = tree_meta[1] + current_uint_masked >> 24;
                    }
                }
            }
            if (!was_24)
            {
                input.Position = byte_offset;
                uint next_byte = input.ReadValueU8();
                byte_offset += 1;
                current_uint += (next_byte << (byte)bit_left);
            }
            if (bit_left >= 8)
            {
                bit_left -= 8;
                uint next_byte = input.ReadValueU8();
                byte_offset += 1;
                current_uint += (next_byte << (byte)bit_left);
            }

            return tree_position;
        }

        private static String ToString(List<UInt16> text)
        {
            UInt16[] textData = text.ToArray();
            byte[] bytes = new byte[textData.Length * 2];
            Buffer.BlockCopy(textData, 0, bytes, 0, textData.Length * 2);
            return System.Text.Encoding.Unicode.GetString(bytes);
        }

        private static UInt16[] ToUInt16(String text)
        {
            byte[] bytes = System.Text.Encoding.Unicode.GetBytes(text);
            UInt16[] textData = new UInt16[bytes.Length / 2];
            Buffer.BlockCopy(bytes, 0, textData, 0, bytes.Length);
            return textData;
        }
    }

    public class Table
    {
        public uint first_id;
        public uint offset_length; // 28 bits offset + 4 bits length

        public uint offset;
        public uint length;

        public SubTableMeta[] sub_table_metas;
        public SubTableIds[] sub_table_ids;

        public long Read(Stream input)
        {
            first_id = input.ReadValueU32();
            offset_length = input.ReadValueU32();

            long save = input.Position;

            offset = offset_length >> 4;
            length = offset_length & 15;

            // read meta data for each subtable
            input.Position = offset;
            sub_table_metas = new SubTableMeta[length];
            for (uint i = 0; i < length; ++i)
            {
                sub_table_metas[i] = new SubTableMeta();
                sub_table_metas[i].Read(input);
            }
            // read ids and bit offset (lo and hi) for each subtable
            sub_table_ids = new SubTableIds[length];
            uint block_first_id = first_id;
            long block_position = input.Position;
            for (uint i = 0; i < length; ++i)
            {
                input.Position = block_position;

                block_first_id += sub_table_metas[i].delta_from_prev_id;

                sub_table_ids[i] = new SubTableIds((uint)block_position);
                sub_table_ids[i].Read(ref block_first_id, sub_table_metas[i], input);

                block_position += sub_table_metas[i].size;
            }

            input.Position = save;

            return block_position;
        }
    }

    public class SubTableMeta
    {
        public uint max_id, size, delta_from_prev_id;

        public void Read(Stream input)
        {
            uint first = input.ReadValueU16();
            uint second = input.ReadValueU16();

            uint whole = (first << 16) + second;
            delta_from_prev_id = second;
            if (whole >= 0x80000000)
            {
                size = input.ReadValueU16();
                if (((whole >> 30) & 1) != 0)
                {
                    uint extra = input.ReadValueU16();
                    delta_from_prev_id += (extra << 16);
                }
                max_id = first & 0x3FFF;
            }
            else
            {
                max_id = first >> 7;
                size = (whole >> 12) & 0x7FF;
                delta_from_prev_id &= 0xFFF;
            }
        }
    }

    public class SubTableIds
    {
        public List<List<Id>> ids;

        public uint start;

        public byte[] raw;

        public SubTableIds(uint start)
        {
            this.start = start;
        }

        public void Read(ref uint id_begin, SubTableMeta subTable, Stream input)
        {
            ids = new List<List<Id>>();

            long subtable_ids_begin = input.Position;

            uint id_count = subTable.max_id + 1;
            uint block_64ids_count = (id_count - 1) >> 6;

            ushort[]  block_64ids_offsets = new ushort[block_64ids_count];
            for (uint i = 0; i < block_64ids_count; ++i)
            {
                block_64ids_offsets[i] = input.ReadValueU16();
            }

            for (uint j = 0; j < id_count; j += 64)
            {
                List<Id> ids_in_block = new List<Id>();
                if (j >= 64 && j % 64 == 0)
                {
                    long block_64ids_offset = block_64ids_offsets[(j >> 6) - 1];
                    if (block_64ids_offset == 0)
                    {
                        ids.Add(ids_in_block);
                        continue;
                    }
                    input.Position = subtable_ids_begin + block_64ids_offset;
                }

               
                uint current_size_in_bits = 0;
                uint k = 0;
                while (k < Math.Min(id_count - j, 64))
                {
                    Id new_id = new Id(id_begin + j + k);
                    new_id.Read(ref k, ref current_size_in_bits, input);
                    ids_in_block.Add(new_id);
                }
                // Adjust offsets from the start
                uint delta = ((uint)(input.Position - start)) << 3;
                foreach (Id new_id in ids_in_block)
                {
                    new_id.lo += delta;
                    new_id.hi += delta;
                }
                ids.Add(ids_in_block);
            }

            id_begin += id_count;
        }
    }

    public class Id
    {
        // if is_pseudo then id increment
        // else size increment in bits (encoded.Length << 3)
        public uint increment;

        // calculated
        public uint id, lo, hi;


        public String str;
        public byte[] encoded;
        public bool is_pseudo;

        public Id(uint id)
        {
            this.id = id;
            this.lo = 1;
            this.hi = 0;
            this.is_pseudo = false;
            this.str = null;
            this.encoded = null;
        }

        public void Read(ref uint k, ref uint current_size_in_bits, Stream input)
        {
            uint current_size = input.ReadValueU8();
            if (current_size > 0xF0)
            {
                increment = current_size - 240;
                k += (current_size - 240);
                if (k > 64)
                {
                    Console.WriteLine("Extras! last id=" + id + " skip=" + (current_size - 240) + " extra=" + (k - 64));
                    Environment.Exit(1);
                }
                is_pseudo = true;
            }
            else
            {
                if (current_size == 0xF0)
                {
                    current_size = input.ReadValueU8();
                    current_size = ((current_size << 8) + input.ReadValueU8()) + 5340;
                }
                else if (current_size >= 0xDC)
                {
                    current_size = ((current_size << 8) + input.ReadValueU8()) - 56100;
                }
                if (current_size != 0)
                {
                    current_size = 2 * current_size + 4;
                }
                increment = current_size;
                lo = current_size_in_bits;
                current_size_in_bits += current_size;
                hi = current_size_in_bits;
                ++k;
            }
        }
    }
}
