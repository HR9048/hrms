export const FORMAT_PRESETS = [
  { key: 'any', label: 'Any text', regex: '', hint: '', forceUpper: false },
  { key: 'digits', label: 'Digits only', regex: '\\d+', hint: 'Digits only', forceUpper: false },
  { key: 'alnum', label: 'Letters & digits', regex: '[A-Za-z0-9]+', hint: 'Letters and digits only', forceUpper: false },
  { key: 'uan', label: 'UAN (12 digits)', regex: '\\d{12}', hint: '12 digits, e.g. 100123456789', forceUpper: false },
  { key: 'aadhaar', label: 'Aadhaar (12 digits)', regex: '\\d{12}', hint: '12 digits', forceUpper: false },
  { key: 'pan', label: 'PAN', regex: '[A-Z]{5}[0-9]{4}[A-Z]', hint: 'ABCDE1234F', forceUpper: true },
  { key: 'custom', label: 'Custom pattern…' }
];