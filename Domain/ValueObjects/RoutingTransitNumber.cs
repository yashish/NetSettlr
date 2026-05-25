using Domain.Exceptions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.ValueObjects
{
    /// <summary>
    /// Routing Transit Number (RTN) is a nine-digit code used in the United States to identify financial institutions. 
    /// It is primarily used for electronic funds transfers, such as direct deposits and wire transfers. 
    /// The RTN is assigned by the American Bankers Association (ABA) and is unique to each financial institution. 
    /// The first four digits of the RTN represent the Federal Reserve Routing Symbol, 
    /// the next four digits identify the specific financial institution, 
    /// and the last digit is a checksum used for validation purposes.
    /// 
    /// Vakue object for Routing Transit Number (RTN) with ABA validation and checksum validation.
    /// Validates structure and the Mod-10 check digit on construction.
    /// </summary>
    public sealed class RoutingTransitNumber : IEquatable<RoutingTransitNumber>
    {
        public string Value { get; }
        private RoutingTransitNumber(string value) => Value = value;

        public bool Equals(RoutingTransitNumber? other) => other is not null && Value == other.Value;

        public override bool Equals(object? obj) => obj is RoutingTransitNumber other && Equals(other);

        public override int GetHashCode() => Value.GetHashCode();

        public override string ToString() => Value;

        /// <summary>
        /// Parses and validates an RTN string. Throws <see cref="InvalidRtnException"/> on failure.
        /// </summary>
        public static RoutingTransitNumber Parse(string rtn)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(rtn);

            var trimmed = rtn.Trim();

            if (trimmed.Length != 9 || !trimmed.All(char.IsAsciiDigit))
                throw new InvalidRtnException($"RTN must be exactly 9 digits. Got: '{rtn}'");

            if (!HasValidCheckDigit(trimmed))
                throw new InvalidRtnException($"RTN check digit is invalid: '{rtn}'");

            return new RoutingTransitNumber(trimmed);
        }

        private static bool HasValidCheckDigit(string value)
        {
            if (value.Length != 9 || !value.All(char.IsDigit))
                return false;
            int[] weights = { 3, 7, 1 };
            int sum = 0;
            for (int i = 0; i < 8; i++)
            {
                sum += (value[i] - '0') * weights[i % 3];
            }
            int checkDigit = (10 - (sum % 10)) % 10;
            return checkDigit == (value[8] - '0');
        }


    }
}
