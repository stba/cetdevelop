﻿using System;
using Microsoft.SPOT;
using Microsoft.SPOT.Hardware;

/*
 * Copyright 2012 Mario Vernari (http://cetdevelop.codeplex.com/)
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

/**
 * 20/Apr/2012
 *  Cut off the server-side codec, being not yet implemented
 **/
namespace Cet.Develop.NETMF.IO.Protocols
{
    public class ModbusRtuCodec
        : ModbusCodecBase, IProtocolCodec
    {
        #region IProtocolCodec members

        void IProtocolCodec.ClientEncode(CommDataBase data)
        {
            var client = (ModbusClient)data.OwnerProtocol;
            var command = (ModbusCommand)data.UserData;
            var fncode = command.FunctionCode;

            //encode the command body, if applies
            var body = new ByteArrayWriter();
            var codec = CommandCodecs[fncode];
            if (codec != null)
                codec.ClientEncode(command, body);

            //create a writer for the outgoing data
            var writer = new ByteArrayWriter();

            //unit identifier (address)
            writer.WriteByte(client.Address);

            //function code
            writer.WriteByte(fncode);

            //body data
            writer.WriteBytes(body);

            //CRC-16
            ushort crc = ByteArrayHelpers.CalcCRC16(
                writer.ToArray(),
                0,
                writer.Length);

            writer.WriteInt16LE((short)crc);

            data.OutgoingData = writer.ToReader();
        }



        CommResponse IProtocolCodec.ClientDecode(CommDataBase data)
        {
            var client = (ModbusClient)data.OwnerProtocol;
            var command = (ModbusCommand)data.UserData;
            var incoming = data.IncomingData;
            var bodyLen = incoming.Length - 4;

            //validate address first
            if (bodyLen >= 0 &&
                incoming.ReadByte() == client.Address)
            {
                //extract function code
                var fncode = incoming.ReadByte();

                //extract the message body
                var body = new ByteArrayReader(incoming.ReadBytes(bodyLen));

                //calculate the CRC-16 over the received stream
                ushort crc = ByteArrayHelpers.CalcCRC16(
                    incoming.ToArray(),
                    2,
                    incoming.Length - 2);

                //validate the CRC-16
                if (incoming.ReadInt16LE() == (short)crc)
                {
                    //message looks consistent (although the body can be empty)
                    if ((fncode & 0x7F) == command.FunctionCode)
                    {
                        if (fncode <= 0x7F)
                        {
                            //
                            //encode the command body, if applies
                            var codec = CommandCodecs[fncode];
                            if (codec != null)
                                codec.ClientDecode(command, body);

                            return new CommResponse(
                                data,
                                CommResponse.Ack);
                        }
                        else
                        {
                            //exception
                            command.ExceptionCode = incoming.ReadByte();

                            return new CommResponse(
                                data,
                                CommResponse.Critical);
                        }
                    }
                }
            }

            return new CommResponse(
                data,
                CommResponse.Unknown);
        }



        void IProtocolCodec.ServerEncode(CommDataBase data)
        {
            var server = (ModbusServer)data.OwnerProtocol;
            var command = (ModbusCommand)data.UserData;
            var fncode = command.FunctionCode;

            //encode the command body, if applies
            var body = new ByteArrayWriter();
            var codec = CommandCodecs[fncode];
            if (codec != null)
                codec.ServerEncode(command, body);

            //calculate length field
            var length = (command.ExceptionCode == 0)
                ? 2 + body.Length
                : 3;

            //create a writer for the outgoing data
            var writer = new ByteArrayWriter();

            //transaction-id
            writer.WriteUInt16BE((ushort)command.TransId);

            //unit identifier (address)
            writer.WriteByte(server.Address);

            if (command.ExceptionCode == 0)
            {
                //function code
                writer.WriteByte(command.FunctionCode);

                //body data
                writer.WriteBytes(body);
            }
            else
            {
                //function code
                writer.WriteByte((byte)(command.FunctionCode | 0x80));

                //exception code
                writer.WriteByte(command.ExceptionCode);
            }

            //CRC-16
            ushort crc = ByteArrayHelpers.CalcCRC16(
                writer.ToArray(),
                0,
                writer.Length);

            writer.WriteInt16LE((short)crc);

            data.OutgoingData = writer.ToReader();
        }



        CommResponse IProtocolCodec.ServerDecode(CommDataBase data)
        {
            var server = (ModbusServer)data.OwnerProtocol;
            var incoming = data.IncomingData;

            //validate header first
            var length = incoming.Length;
            if (length < 4)
                goto LabelUnknown;

            //address
            var address = incoming.ReadByte();

            if (address == server.Address)
            {
                //function code
                var fncode = incoming.ReadByte();

                //create a new command
                var command = new ModbusCommand(fncode);
                data.UserData = command;

                //
                var body = new ByteArrayReader(incoming.ReadBytes(length - 4));

                //calculate the CRC-16 over the received stream
                ushort crc = ByteArrayHelpers.CalcCRC16(
                    incoming.ToArray(),
                    0,
                    length - 2);

                //validate the CRC-16
                if (incoming.ReadInt16LE() == (short)crc)
                {
                    //encode the command body, if applies
                    var codec = CommandCodecs[fncode];
                    if (codec != null)
                        codec.ServerDecode(command, body);

                    return new CommResponse(
                        data,
                        CommResponse.Ack);
                }
            }

            //exception
            return new CommResponse(
                data,
                CommResponse.Ignore);

        LabelUnknown:
            return new CommResponse(
                data,
                CommResponse.Unknown);
        }

        #endregion

    }
}
