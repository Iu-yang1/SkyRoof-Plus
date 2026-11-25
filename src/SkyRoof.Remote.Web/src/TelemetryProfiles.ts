export type TelemetryConfig = {
  enabled: boolean; noradId: number; satellite: string; transmitter: string; modulation: string;
  framing: string; baud: number; input: string; centerHz: number; deviationHz: number;
  differential: boolean; manchester: boolean; downlinkHz: number; afCarrierHz: number;
  convolutional: string; frameSize: number; rsInterleaving: number; rsBasis: string;
};
const base: TelemetryConfig = { enabled: false, noradId: 39444, satellite: 'AO-73', transmitter: 'Telemetry',
  modulation: 'BPSK', framing: 'AO40FEC', baud: 1200, input: 'tone-audio', centerHz: 1700,
  deviationHz: 0, differential: true, manchester: false, downlinkHz: 0, afCarrierHz: 0,
  convolutional: '', frameSize: 0, rsInterleaving: 0, rsBasis: '' };
export const telemetryProfiles = {
  'AO-73': base,
  'AO-27': { ...base, noradId: 22825, satellite: 'AO-27', modulation: 'AFSK', framing: 'AX25G3RUH',
    input: 'fm-discriminator', deviationHz: 500, afCarrierHz: 1700, differential: false },
  'UNNE-1B': { ...base, noradId: 101101, satellite: 'UNNE-1B', modulation: 'FSK', framing: 'HADES',
    baud: 200, input: 'fm-discriminator', deviationHz: 820, differential: false, downlinkHz: 436888000 }
};
