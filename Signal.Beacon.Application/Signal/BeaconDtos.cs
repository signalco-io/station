using System;
using System.Collections.Generic;

namespace Signal.Beacon.Application.Signal;

public record SignalBeaconRegisterRequestDto(string BeaconId);

public record SignalBeaconRefreshTokenRequestDto(string RefreshToken);

public record SignalBeaconRefreshTokenResponseDto(string AccessToken, DateTime Expire);

public record SignalcoLoggingStationRequestDto(string StationId, IEnumerable<SignalcoLoggingStationEntryDto> Entries);

public record SignalcoLoggingStationEntryDto(DateTimeOffset T, int L, string M);