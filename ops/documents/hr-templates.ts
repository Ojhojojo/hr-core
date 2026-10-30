import { jsPDF } from 'jspdf';
import { ShopInfo, formatPhp } from '@hr-host';
import { EmployeeCard } from '../employee-directory/employee-directory.api';
import { formatLetterDate } from '../employee-directory/employment-letter';
import { pdfText } from '../pdf-text';

export type HrDocumentKind = 'nte' | 'nod' | 'quitclaim' | 'warning';
/** Templates that open their own dialog instead of the letter editor. */
export type HrTemplateKind = HrDocumentKind | 'bir-2316';
export type NodOutcome = 'warning' | 'suspension' | 'termination';
export type WarningLevel = 'verbal' | 'first' | 'final';

export const DEFAULT_LABOR_CITE = 'Article 297 of the Labor Code of the Philippines';
export const DEFAULT_RESPONSE_DAYS = 5;
export const DEFAULT_WARNING_LEVEL: WarningLevel = 'first';
export const BLANK_LINE = '________________';

export interface HrTemplateRow {
  id: string;
  title: string;
  description: string;
  status: 'available' | 'coming_soon';
  kind?: HrTemplateKind;
}

export const HR_TEMPLATES: HrTemplateRow[] = [
  {
    id: 'nte',
    title: 'Notice to explain',
    description:
      'Ask a worker to explain an incident. Includes a response window, a hearing, and signature blanks.',
    status: 'available',
    kind: 'nte',
  },
  {
    id: 'nod',
    title: 'Notice of decision',
    description:
      'Record a warning, a suspension, or termination, with the decision and effective date.',
    status: 'available',
    kind: 'nod',
  },
  {
    id: 'quitclaim',
    title: 'Quitclaim and release',
    description:
      'Separation acknowledgment with a final-pay amount you type in. Nothing here calculates pay.',
    status: 'available',
    kind: 'quitclaim',
  },
  {
    id: 'warning',
    title: 'Warning letter',
    description: 'A standalone warning, separate from a notice of decision.',
    status: 'available',
    kind: 'warning',
  },
  {
    id: 'bir-2307',
    title: 'BIR Form 2307',
    description: 'Certificate of creditable tax withheld at source.',
    status: 'coming_soon',
  },
  {
    id: 'bir-2316',
    title: 'BIR Form 2316',
    description:
      'Data sheet for one employee and year: the values to copy into BIR Form 2316, from locked payroll runs.',
    status: 'available',
    kind: 'bir-2316',
  },
];

export interface HrDocumentDraft {
  kind: HrDocumentKind;
  issuedOn: string;
  tradingName: string;
  legalName: string;
  tin: string;
  address: string;
  phone: string;
  workerName: string;
  position: string;
  department: string;
  employeeNo: string;
  workerTin: string;
  sss: string;
  philHealth: string;
  pagIbig: string;
  incidentDate: string;
  incidentPlace: string;
  infraction: string;
  codeCite: string;
  responseDays: number | string;
  hearingDate: string;
  hearingVenue: string;
  outcome: NodOutcome;
  suspensionDays: string;
  decisionSummary: string;
  effectiveDate: string;
  separationDate: string;
  finalPayPhp: string;
  warningLevel: WarningLevel;
  ruleViolated: string;
  incidentDescription: string;
  correctiveAction: string;
  repeatConsequence: string;
  relatedNte: string;
  issuerName: string;
  issuerPosition: string;
}

/** Default "if repeated" wording for each warning level. */
export function defaultRepeatConsequence(level: WarningLevel): string {
  switch (level) {
    case 'verbal':
      return 'A repeat of this conduct may lead to a first written warning.';
    case 'first':
      return 'A repeat of this conduct may lead to a final written warning.';
    case 'final':
      return 'A repeat of this conduct may lead to disciplinary action up to and including termination of employment, after due process.';
  }
}

/**
 * Called when the warning level changes. Swaps the consequence text only when it
 * still holds the previous level's default, so wording the user typed is kept.
 */
export function nextRepeatConsequence(
  current: string,
  previous: WarningLevel,
  next: WarningLevel,
): string {
  const trimmed = current.trim();
  if (!trimmed || trimmed === defaultRepeatConsequence(previous)) {
    return defaultRepeatConsequence(next);
  }
  return current;
}

export function emptyHrDraft(
  kind: HrDocumentKind,
  shop: ShopInfo | null,
  person: EmployeeCard | null,
  issuedOn = '',
): HrDocumentDraft {
  const draft: HrDocumentDraft = {
    kind,
    issuedOn,
    tradingName: '',
    legalName: '',
    tin: '',
    address: '',
    phone: '',
    workerName: '',
    position: '',
    department: '',
    employeeNo: '',
    workerTin: '',
    sss: '',
    philHealth: '',
    pagIbig: '',
    incidentDate: issuedOn,
    incidentPlace: '',
    infraction: '',
    codeCite: DEFAULT_LABOR_CITE,
    responseDays: DEFAULT_RESPONSE_DAYS,
    hearingDate: '',
    hearingVenue: '',
    outcome: 'warning',
    suspensionDays: '',
    decisionSummary: '',
    effectiveDate: issuedOn,
    separationDate: issuedOn,
    finalPayPhp: '',
    warningLevel: DEFAULT_WARNING_LEVEL,
    ruleViolated: '',
    incidentDescription: '',
    correctiveAction: '',
    repeatConsequence: defaultRepeatConsequence(DEFAULT_WARNING_LEVEL),
    relatedNte: '',
    issuerName: '',
    issuerPosition: '',
  };
  if (shop) {
    applyShopLetterhead(draft, shop);
  }
  if (person) {
    applyWorker(draft, person);
  }
  return draft;
}

export function applyShopLetterhead(draft: HrDocumentDraft, shop: ShopInfo): void {
  const trading = shop.tradingName?.trim() || 'Rakkii Cars';
  const legal = shop.legalName?.trim() || trading;
  if (!draft.tradingName.trim()) {
    draft.tradingName = trading;
  }
  if (!draft.legalName.trim()) {
    draft.legalName = legal;
  }
  if (!draft.tin.trim()) {
    draft.tin = shop.tin?.trim() || '';
  }
  if (!draft.address.trim()) {
    draft.address = shop.address?.trim() ?? '';
  }
  if (!draft.phone.trim()) {
    draft.phone = shop.phone?.trim() ?? '';
  }
  if (!draft.incidentPlace.trim()) {
    draft.incidentPlace = draft.address;
  }
  if (!draft.hearingVenue.trim()) {
    draft.hearingVenue = draft.address;
  }
}

export function applyWorker(draft: HrDocumentDraft, person: EmployeeCard): void {
  draft.workerName = person.displayName;
  draft.position = person.position?.trim() || person.role;
  draft.department = person.department?.trim() ?? '';
  draft.employeeNo = person.employeeNo?.trim() ?? '';
  draft.workerTin = person.tin?.trim() ?? '';
  draft.sss = person.sss?.trim() ?? '';
  draft.philHealth = person.philHealth?.trim() ?? '';
  draft.pagIbig = person.pagIbig?.trim() ?? '';
}

export function documentTitle(kind: HrDocumentKind): string {
  switch (kind) {
    case 'nte':
      return 'Notice to explain';
    case 'nod':
      return 'Notice of decision';
    case 'quitclaim':
      return 'Quitclaim and release';
    case 'warning':
      return 'Warning letter';
  }
}

export function signatureLabels(kind: HrDocumentKind): string[] {
  switch (kind) {
    case 'nte':
      return ['Employee', 'Human resources', 'Department head', 'Witness'];
    case 'nod':
      return ['Employee', 'Human resources', 'Department head'];
    case 'quitclaim':
      return ['Employee', 'Human resources', 'Witness'];
    case 'warning':
      return ['Issued by', 'Received by'];
  }
}

/**
 * Lines printed under a signature label. The employee (or the warning's
 * "Received by") gets the worker's name; the warning issuer gets name and position.
 */
export function signatureDetailLines(draft: HrDocumentDraft, label: string): string[] {
  if (label === 'Employee' || label === 'Received by') {
    return draft.workerName.trim() ? [draft.workerName.trim()] : [];
  }
  if (label === 'Issued by') {
    return [draft.issuerName.trim(), draft.issuerPosition.trim()].filter(Boolean);
  }
  return [];
}

export function responseDayCount(value: number | string): number {
  const count = Math.floor(Number(value));
  if (!Number.isFinite(count) || count < 1) {
    return DEFAULT_RESPONSE_DAYS;
  }
  return count;
}

export function addCalendarDays(iso: string, days: number): string {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(iso.trim());
  if (!match) {
    return '';
  }
  const date = new Date(Date.UTC(Number(match[1]), Number(match[2]) - 1, Number(match[3])));
  date.setUTCDate(date.getUTCDate() + days);
  return date.toISOString().slice(0, 10);
}

export function formatManualPhp(raw: string): string {
  const trimmed = raw.trim();
  if (!trimmed) {
    return '';
  }
  const numeric = Number(
    trimmed
      .replace(/,/g, '')
      .replace(/^₱\s*/, '')
      .replace(/^PHP\s*/i, ''),
  );
  if (!Number.isFinite(numeric)) {
    return trimmed;
  }
  return formatPhp(numeric);
}

export function documentParagraphs(draft: HrDocumentDraft): string[] {
  switch (draft.kind) {
    case 'nte':
      return nteParagraphs(draft);
    case 'nod':
      return nodParagraphs(draft);
    case 'quitclaim':
      return quitclaimParagraphs(draft);
    case 'warning':
      return warningParagraphs(draft);
  }
}

export function renderHrDocumentHtml(draft: HrDocumentDraft): string {
  const title = escapeHtml(documentTitle(draft.kind));
  const heading = escapeHtml(draft.tradingName.trim() || draft.legalName.trim() || 'Rakkii Cars');
  const legal =
    draft.legalName.trim() && draft.legalName.trim() !== draft.tradingName.trim()
      ? `<p>${escapeHtml(draft.legalName.trim())}</p>`
      : '';
  const tin = draft.tin.trim() ? `<p>TIN ${escapeHtml(draft.tin.trim())}</p>` : '';
  const contact = [draft.address.trim(), draft.phone.trim()].filter(Boolean).join(' · ');
  const contactHtml = contact ? `<p>${escapeHtml(contact)}</p>` : '';
  const body = documentParagraphs(draft)
    .map((line) => `<p>${escapeHtml(line)}</p>`)
    .join('\n');
  const signs = signatureLabels(draft.kind)
    .map((label) => {
      const details = signatureDetailLines(draft, label)
        .map((line) => `<p>${escapeHtml(line)}</p>`)
        .join('');
      return `<div class="sign"><div class="line"></div><p>${escapeHtml(label)}</p>${details}</div>`;
    })
    .join('\n');
  return `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8" />
<title>${title}</title>
<style>
  @page { size: A4; margin: 16mm; }
  body { font-family: Georgia, 'Times New Roman', serif; color: #1a1a1a; }
  h1 { font-size: 22px; margin: 0 0 4px; }
  h2 { font-size: 18px; margin: 24px 0 8px; }
  p { line-height: 1.45; }
  .signs { display: grid; grid-template-columns: 1fr 1fr; gap: 28px; margin-top: 36px; }
  .line { border-bottom: 1px solid #111; height: 36px; }
</style>
</head>
<body>
  <h1>${heading}</h1>
  ${legal}
  ${tin}
  ${contactHtml}
  <h2>${title}</h2>
  ${body}
  <div class="signs">
    ${signs}
  </div>
</body>
</html>`;
}

export function saveNote(kind: HrDocumentKind): string {
  switch (kind) {
    case 'nte':
      return 'Notice to explain';
    case 'nod':
      return 'Notice of decision';
    case 'quitclaim':
      return 'Quitclaim';
    case 'warning':
      return 'Warning letter';
  }
}

export function saveFilePrefix(kind: HrDocumentKind): string {
  return kind;
}

export function hrDocumentFileSlug(draft: HrDocumentDraft): string {
  return (
    draft.workerName
      .trim()
      .toLowerCase()
      .replace(/[^a-z0-9]+/g, '-')
      .replace(/^-|-$/g, '') || 'staff'
  );
}

export function hrDocumentPdfFileName(draft: HrDocumentDraft): string {
  return `${saveFilePrefix(draft.kind)}-${hrDocumentFileSlug(draft)}.pdf`;
}

/** A4 PDF from draft fields — used for Print and Download PDF. Save still uses HTML. */
export function buildHrDocumentPdf(draft: HrDocumentDraft): Blob {
  const doc = new jsPDF({ unit: 'mm', format: 'a4' });
  const margin = 16;
  const pageWidth = doc.internal.pageSize.getWidth();
  const pageHeight = doc.internal.pageSize.getHeight();
  const maxWidth = pageWidth - margin * 2;
  const lineGap = 5.2;
  let y = margin;

  const ensureSpace = (needed: number) => {
    if (y + needed > pageHeight - margin) {
      doc.addPage();
      y = margin;
    }
  };

  const writeWrapped = (text: string, fontSize: number, style: 'normal' | 'bold' = 'normal') => {
    doc.setFont('times', style);
    doc.setFontSize(fontSize);
    const lines = doc.splitTextToSize(pdfText(text), maxWidth) as string[];
    ensureSpace(lines.length * lineGap + 2);
    doc.text(lines, margin, y);
    y += lines.length * lineGap;
  };

  const heading = draft.tradingName.trim() || draft.legalName.trim() || 'Rakkii Cars';
  writeWrapped(heading, 16, 'bold');
  y += 1;

  if (draft.legalName.trim() && draft.legalName.trim() !== draft.tradingName.trim()) {
    writeWrapped(draft.legalName.trim(), 11);
  }
  if (draft.tin.trim()) {
    writeWrapped(`TIN ${draft.tin.trim()}`, 11);
  }
  const place = [draft.address.trim(), draft.phone.trim()].filter(Boolean).join(' · ');
  if (place) {
    writeWrapped(place, 11);
  }

  y += 6;
  writeWrapped(documentTitle(draft.kind), 14, 'bold');
  y += 4;

  for (const para of documentParagraphs(draft)) {
    writeWrapped(para, 11);
    y += 3;
  }

  y += 10;
  const colGap = 12;
  const colWidth = (maxWidth - colGap) / 2;
  const labels = signatureLabels(draft.kind);
  for (let i = 0; i < labels.length; i += 2) {
    const rowLabels = labels.slice(i, i + 2);
    ensureSpace(28);
    const rowTop = y;
    doc.setDrawColor(17, 17, 17);
    rowLabels.forEach((label, col) => {
      const x = margin + col * (colWidth + colGap);
      doc.line(x, rowTop, x + Math.min(60, colWidth), rowTop);
      doc.setFont('times', 'normal');
      doc.setFontSize(11);
      doc.text(pdfText(label), x, rowTop + 7);
      signatureDetailLines(draft, label).forEach((line, index) => {
        doc.text(pdfText(line), x, rowTop + 13 + index * 6);
      });
    });
    y = rowTop + 22;
  }

  return doc.output('blob');
}

function nteParagraphs(draft: HrDocumentDraft): string[] {
  const company = companyName(draft);
  const worker = draft.workerName.trim() || 'the employee';
  const days = responseDayCount(draft.responseDays);
  const deadline = draft.issuedOn ? addCalendarDays(draft.issuedOn, days) : '';
  const received = draft.issuedOn ? formatLetterDate(draft.issuedOn) : 'the date of this notice';
  const through = deadline ? formatLetterDate(deadline) : 'the end of that window';
  const incident = draft.incidentDate ? formatLetterDate(draft.incidentDate) : '________________';
  const place = draft.incidentPlace.trim() || '________________';
  const infraction = draft.infraction.trim() || '________________';
  const cite = draft.codeCite.trim() || DEFAULT_LABOR_CITE;
  const hearing = draft.hearingDate ? formatLetterDate(draft.hearingDate) : 'a date to be set';
  const venue = draft.hearingVenue.trim() || 'a venue to be set';

  return [
    draft.issuedOn ? formatLetterDate(draft.issuedOn) : '',
    `Dear ${worker},`,
    workerIntro(draft),
    statutorySentence(draft),
    `${company} requires your written explanation of the following.`,
    `Date of incident: ${incident}.`,
    `Place: ${place}.`,
    `Infraction: ${infraction}.`,
    `This notice cites ${cite}.`,
    `You have ${days} calendar days from receipt of this notice to submit your written explanation. If you receive this notice on ${received}, that window runs through ${through}.`,
    `Hearing date: ${hearing}. Venue: ${venue}.`,
    'If you do not respond within that window, the shop may decide from the records it already has.',
  ].filter(Boolean);
}

function nodParagraphs(draft: HrDocumentDraft): string[] {
  const company = companyName(draft);
  const worker = draft.workerName.trim() || 'the employee';
  const effective = draft.effectiveDate
    ? formatLetterDate(draft.effectiveDate)
    : '________________';
  const summary = draft.decisionSummary.trim() || '________________';

  return [
    draft.issuedOn ? formatLetterDate(draft.issuedOn) : '',
    `Dear ${worker},`,
    workerIntro(draft),
    statutorySentence(draft),
    `After review of the records, ${company} has reached this decision. This notice states the outcome only. It is not minutes of a hearing.`,
    nodOutcomeSentence(draft),
    `Effective date: ${effective}.`,
    `Decision: ${summary}.`,
    'Please sign below to acknowledge that you received this notice.',
  ].filter(Boolean);
}

function quitclaimParagraphs(draft: HrDocumentDraft): string[] {
  const company = companyName(draft);
  const worker = draft.workerName.trim() || 'the undersigned';
  const position = draft.position.trim() || 'staff';
  const separated = draft.separationDate
    ? formatLetterDate(draft.separationDate)
    : '________________';
  const amount = formatManualPhp(draft.finalPayPhp) || 'PHP ________________';

  return [
    draft.issuedOn ? formatLetterDate(draft.issuedOn) : '',
    `I, ${worker}, ${position} at ${company}, state the following.`,
    quitclaimIdentity(draft),
    statutorySentence(draft),
    `My employment ended on ${separated}.`,
    `I acknowledge receipt of final pay in the amount of ${amount}. The amount is the figure written on this document.`,
    `I acknowledge that this payment settles my wages and any separation benefits that may be due, including benefits under Republic Act No. 7641 where that law applies, and I release ${company} from further money claims arising from my employment, except claims that the law does not allow me to waive.`,
    'I sign this quitclaim freely.',
  ].filter(Boolean);
}

function warningParagraphs(draft: HrDocumentDraft): string[] {
  const company = companyName(draft);
  const worker = draft.workerName.trim() || 'the employee';
  const incident = draft.incidentDate ? formatLetterDate(draft.incidentDate) : BLANK_LINE;
  const rule = draft.ruleViolated.trim() || BLANK_LINE;
  const related = draft.relatedNte.trim();

  return [
    draft.issuedOn ? formatLetterDate(draft.issuedOn) : '',
    `Dear ${worker},`,
    workerIntro(draft),
    // No government numbers here: a warning letter doesn't need TIN, SSS, PhilHealth or Pag-IBIG.
    warningLevelSentence(draft.warningLevel, company),
    `Date of incident: ${incident}.`,
    `Rule or policy violated: ${rule}.`,
    'What happened:',
    ...multilineOrBlank(draft.incidentDescription),
    'What is expected of you from here:',
    ...multilineOrBlank(draft.correctiveAction),
    'If this conduct is repeated:',
    ...multilineOrBlank(draft.repeatConsequence),
    related ? `This warning relates to the notice to explain: ${related}.` : '',
    'Please sign below to acknowledge that you received this letter. Signing acknowledges receipt only. It does not mean that you agree with its contents.',
    `Date received: ${BLANK_LINE}.`,
  ].filter(Boolean);
}

function warningLevelSentence(level: WarningLevel, company: string): string {
  switch (level) {
    case 'verbal':
      return `${company} is putting on record a verbal warning given to you. This letter documents it in writing.`;
    case 'first':
      return `${company} is issuing you a first written warning.`;
    case 'final':
      return `${company} is issuing you a final written warning.`;
  }
}

/** Splits a multi-line field into one paragraph per non-empty line. */
export function multilineOrBlank(text: string): string[] {
  const lines = text
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter(Boolean);
  return lines.length ? lines : [BLANK_LINE];
}

function nodOutcomeSentence(draft: HrDocumentDraft): string {
  switch (draft.outcome) {
    case 'warning':
      return 'Outcome: written warning.';
    case 'termination':
      return 'Outcome: termination of employment.';
    case 'suspension': {
      const days = draft.suspensionDays.trim();
      return days
        ? `Outcome: suspension for ${days} calendar day(s).`
        : 'Outcome: suspension. The number of days is blank on this copy.';
    }
  }
}

function companyName(draft: HrDocumentDraft): string {
  return draft.legalName.trim() || draft.tradingName.trim() || 'the company';
}

function quitclaimIdentity(draft: HrDocumentDraft): string {
  const bits = [
    draft.department.trim(),
    draft.employeeNo.trim() ? `employee no. ${draft.employeeNo.trim()}` : '',
  ].filter(Boolean);
  if (!bits.length) {
    return '';
  }
  return `My record shows ${bits.join(', ')}.`;
}

function workerIntro(draft: HrDocumentDraft): string {
  const bits = [
    draft.position.trim(),
    draft.department.trim(),
    draft.employeeNo.trim() ? `employee no. ${draft.employeeNo.trim()}` : '',
  ].filter(Boolean);
  if (!bits.length) {
    return '';
  }
  return `You are addressed here as ${bits.join(', ')}.`;
}

function statutorySentence(draft: HrDocumentDraft): string {
  const bits = [
    draft.workerTin.trim() ? `TIN ${draft.workerTin.trim()}` : '',
    draft.sss.trim() ? `SSS ${draft.sss.trim()}` : '',
    draft.philHealth.trim() ? `PhilHealth ${draft.philHealth.trim()}` : '',
    draft.pagIbig.trim() ? `Pag-IBIG ${draft.pagIbig.trim()}` : '',
  ].filter(Boolean);
  if (!bits.length) {
    return '';
  }
  return `Government numbers on file: ${bits.join('; ')}.`;
}

function escapeHtml(value: string): string {
  return value
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;');
}
