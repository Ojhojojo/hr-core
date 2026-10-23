import { describe, expect, it } from 'vitest';
import { ShopInfo } from '@hr-host';
import { EmployeeCard } from '../employee-directory/employee-directory.api';
import {
  BLANK_LINE,
  DEFAULT_LABOR_CITE,
  DEFAULT_RESPONSE_DAYS,
  HR_TEMPLATES,
  addCalendarDays,
  applyWorker,
  buildHrDocumentPdf,
  defaultRepeatConsequence,
  documentParagraphs,
  documentTitle,
  emptyHrDraft,
  formatManualPhp,
  hrDocumentPdfFileName,
  multilineOrBlank,
  nextRepeatConsequence,
  renderHrDocumentHtml,
  responseDayCount,
  saveNote,
  signatureDetailLines,
  signatureLabels,
} from './hr-templates';

const shop: ShopInfo = {
  tradingName: 'Harbor Lane',
  legalName: 'Harbor Lane Motors OPC',
  address: '1 Harbor Road',
  phone: '0917-111-2222',
  facebook: '',
  email: 'shop@example.com',
  tin: '111-222-333-000',
};

const person: EmployeeCard = {
  id: 'p-1',
  role: 'technician',
  displayName: 'Ana Active',
  email: null,
  mobile: null,
  hireDate: '2024-02-01',
  active: true,
  emergencyContactName: null,
  emergencyContactPhone: null,
  position: 'Lead painter',
  department: 'Body and paint',
  employeeNo: 'RC-014',
  tin: '123-456-789-000',
  sss: '01-2345678-9',
  philHealth: '12-345678901-2',
  pagIbig: '1234-5678-9012',
};

describe('HR document templates', () => {
  it('lists NTE, NOD, quitclaim, warning, and BIR 2316 as available and leaves the rest inert', () => {
    expect(HR_TEMPLATES.filter((row) => row.status === 'available').map((row) => row.kind)).toEqual(
      ['nte', 'nod', 'quitclaim', 'warning', 'bir-2316'],
    );
    const coming = HR_TEMPLATES.filter((row) => row.status === 'coming_soon');
    expect(coming.map((row) => row.id)).toEqual(['bir-2307']);
    expect(HR_TEMPLATES.some((row) => row.id === 'show-cause')).toBe(false);
    expect(coming.every((row) => row.kind == null)).toBe(true);
    expect(HR_TEMPLATES.map((row) => row.title).join(' ')).not.toMatch(/waiver|intake|job order/i);
  });

  it('builds an NTE with the default cite, a 5-day window, and four signature blanks', () => {
    const draft = emptyHrDraft('nte', shop, person, '2026-10-01');
    draft.incidentDate = '2026-09-28';
    draft.incidentPlace = 'Paint booth';
    draft.infraction = 'Left the booth unattended';
    draft.hearingDate = '2026-10-08';
    draft.hearingVenue = 'Office';

    expect(draft.codeCite).toBe(DEFAULT_LABOR_CITE);
    expect(draft.responseDays).toBe(DEFAULT_RESPONSE_DAYS);
    expect(addCalendarDays('2026-10-01', 5)).toBe('2026-10-06');
    expect(addCalendarDays('2026-10-29', 5)).toBe('2026-11-03');
    expect(addCalendarDays('nope', 5)).toBe('');
    expect(responseDayCount(0)).toBe(5);

    const text = documentParagraphs(draft).join(' ');
    expect(text).toContain('October 1, 2026');
    expect(text).toContain('Ana Active');
    expect(text).toContain('Lead painter');
    expect(text).toContain('September 28, 2026');
    expect(text).toContain('Paint booth');
    expect(text).toContain('Left the booth unattended');
    expect(text).toContain('Article 297 of the Labor Code of the Philippines');
    expect(text).toContain('5 calendar days');
    expect(text).toContain('October 6, 2026');
    expect(text).toContain('October 8, 2026');
    expect(text).toContain('Office');
    expect(text).toContain('TIN 123-456-789-000');
    expect(text).toContain('Harbor Lane Motors OPC');
    expect(signatureLabels('nte')).toEqual([
      'Employee',
      'Human resources',
      'Department head',
      'Witness',
    ]);
  });

  it('states a warning, a counted suspension, or termination without hearing minutes', () => {
    const warning = emptyHrDraft('nod', shop, person, '2026-10-02');
    warning.effectiveDate = '2026-10-03';
    warning.decisionSummary = 'First written warning for the booth incident.';
    warning.suspensionDays = '9';
    const warningText = documentParagraphs(warning).join(' ');
    expect(warningText).toContain('Outcome: written warning.');
    expect(warningText).toContain('October 3, 2026');
    expect(warningText).toContain('First written warning for the booth incident.');
    expect(warningText).toContain('not minutes of a hearing');
    expect(warningText).not.toContain('9 calendar');

    const suspended = { ...warning, outcome: 'suspension' as const, suspensionDays: '3' };
    expect(documentParagraphs(suspended).join(' ')).toContain('suspension for 3 calendar day(s)');

    const ended = { ...warning, outcome: 'termination' as const };
    expect(documentParagraphs(ended).join(' ')).toContain('termination of employment');
    expect(signatureLabels('nod')).toEqual(['Employee', 'Human resources', 'Department head']);
  });

  it('prefills the repeat consequence by warning level and keeps wording the user typed', () => {
    expect(defaultRepeatConsequence('final')).toContain(
      'disciplinary action up to and including termination of employment, after due process.',
    );
    expect(defaultRepeatConsequence('verbal')).toContain('first written warning');
    expect(defaultRepeatConsequence('first')).toContain('final written warning');

    const fromDefault = nextRepeatConsequence(defaultRepeatConsequence('first'), 'first', 'final');
    expect(fromDefault).toBe(defaultRepeatConsequence('final'));

    const edited = 'Repeat means a suspension of three days.';
    expect(nextRepeatConsequence(edited, 'first', 'final')).toBe(edited);
    expect(nextRepeatConsequence('   ', 'verbal', 'first')).toBe(defaultRepeatConsequence('first'));
  });

  it('builds a warning letter with the level, facts, expectations, and receipt-only acknowledgment', () => {
    const draft = emptyHrDraft('warning', shop, person, '2026-10-10');
    draft.warningLevel = 'final';
    draft.repeatConsequence = defaultRepeatConsequence('final');
    draft.incidentDate = '2026-10-08';
    draft.ruleViolated = 'Shop policy 4.2 on booth attendance';
    draft.incidentDescription =
      'Left the booth unattended for forty minutes.\n\nNo handover was logged.';
    draft.correctiveAction = 'Stay at the booth until relieved.\nLog every handover.';
    draft.relatedNte = 'NTE dated October 1, 2026';
    draft.issuerName = 'Juan Dela Cruz';
    draft.issuerPosition = 'Shop manager';

    expect(documentTitle('warning')).toBe('Warning letter');
    expect(saveNote('warning')).toBe('Warning letter');
    expect(signatureLabels('warning')).toEqual(['Issued by', 'Received by']);
    expect(signatureDetailLines(draft, 'Issued by')).toEqual(['Juan Dela Cruz', 'Shop manager']);
    expect(signatureDetailLines(draft, 'Received by')).toEqual(['Ana Active']);

    const paragraphs = documentParagraphs(draft);
    const text = paragraphs.join(' ');
    expect(paragraphs[0]).toBe('October 10, 2026');
    expect(text).toContain('Dear Ana Active,');
    expect(text).toContain('Harbor Lane Motors OPC is issuing you a final written warning.');
    expect(text).toContain('Date of incident: October 8, 2026.');
    expect(text).toContain('Rule or policy violated: Shop policy 4.2 on booth attendance.');
    expect(paragraphs).toContain('Left the booth unattended for forty minutes.');
    expect(paragraphs).toContain('No handover was logged.');
    expect(paragraphs).toContain('Stay at the booth until relieved.');
    expect(paragraphs).toContain('Log every handover.');
    expect(text).toContain('termination of employment, after due process.');
    expect(text).toContain(
      'This warning relates to the notice to explain: NTE dated October 1, 2026.',
    );
    expect(text).toContain('does not mean that you agree');
    expect(text).toContain(`Date received: ${BLANK_LINE}.`);
    // The person has TIN, SSS, PhilHealth and Pag-IBIG on file; a warning letter never prints them.
    expect(text).not.toContain('Government numbers');
    expect(text).not.toContain(person.tin!);
    expect(text).not.toContain(person.sss!);
  });

  it('leaves blanks for an unfilled warning and omits the related NTE line when empty', () => {
    const draft = emptyHrDraft('warning', shop, null, '');
    draft.warningLevel = 'verbal';
    const text = documentParagraphs(draft).join(' ');
    expect(text).toContain('putting on record a verbal warning');
    expect(text).toContain(`Rule or policy violated: ${BLANK_LINE}.`);
    expect(text).toContain(`Date of incident: ${BLANK_LINE}.`);
    expect(text).not.toContain('relates to the notice to explain');
    expect(multilineOrBlank('  \n ')).toEqual([BLANK_LINE]);
    expect(signatureDetailLines(draft, 'Issued by')).toEqual([]);
  });

  it('prints a quitclaim from the typed final-pay amount', () => {
    const draft = emptyHrDraft('quitclaim', shop, person, '2026-10-07');
    draft.separationDate = '2026-10-04';
    draft.finalPayPhp = '15000';
    const text = documentParagraphs(draft).join(' ');
    expect(formatManualPhp('15000')).toBe('PHP 15,000.00');
    expect(formatManualPhp('see payroll')).toBe('see payroll');
    expect(text).toContain('Ana Active');
    expect(text).toContain('Lead painter');
    expect(text).toContain('October 4, 2026');
    expect(text).toContain('PHP 15,000.00');
    expect(text).toContain('Republic Act No. 7641');
    expect(text).not.toMatch(/13th/i);
    expect(text).not.toMatch(/computed|formula/i);
    expect(signatureLabels('quitclaim')).toEqual(['Employee', 'Human resources', 'Witness']);
  });

  it('escapes the saved letter and keeps the shop letterhead', () => {
    const draft = emptyHrDraft('nte', shop, null, '2026-10-01');
    applyWorker(draft, person);
    draft.infraction = '<script>alert(1)</script>';
    const html = renderHrDocumentHtml(draft);
    expect(html).toContain('Harbor Lane');
    expect(html).toContain('Harbor Lane Motors OPC');
    expect(html).toContain('TIN 111-222-333-000');
    expect(html).toContain('&lt;script&gt;alert(1)&lt;/script&gt;');
    expect(html).not.toContain('<script>');
    expect(html).toContain('Witness');
  });

  it('builds a printable PDF blob with a %PDF header', async () => {
    const draft = emptyHrDraft('nte', shop, person, '2026-10-01');
    draft.incidentDate = '2026-09-28';
    draft.incidentPlace = 'Paint booth';
    draft.infraction = 'Left the booth unattended';
    expect(hrDocumentPdfFileName(draft)).toBe('nte-ana-active.pdf');

    const blob = buildHrDocumentPdf(draft);
    expect(blob.type).toBe('application/pdf');
    expect(blob.size).toBeGreaterThan(100);
    const header = new TextDecoder().decode(await blob.slice(0, 4).arrayBuffer());
    expect(header).toBe('%PDF');
  });
});
