﻿using MimeKit;
using System.IO;
using System.Text.RegularExpressions;

namespace SharpNGDP.Ribbit
{
    public class RibbitFile : NGDPFile
    {
        private Regex s_ChecksumRegex = new Regex(@"Checksum: (\w+)", RegexOptions.Compiled);

        public RibbitFile(Stream stream)
            : base(stream)
        { }

        public string Checksum { get; private set; }

        public MimeMessage MimeMessage { get; private set; }

        public override void Read()
        {
            MimeMessage = MimeMessage.Load(Stream);
            // throw new EmptyRibbitResponseException("Invalid response from server. Likely caused by malformed request.");
            var checksumMatch = s_ChecksumRegex.Match(((MultipartAlternative)MimeMessage.Body).Epilogue);
            if (!checksumMatch.Success)
                throw new MalformedRibbitResponseException("Response did not contain checksum in epilogue");
            Checksum = checksumMatch.Groups[1].Value;
        }
    }
}