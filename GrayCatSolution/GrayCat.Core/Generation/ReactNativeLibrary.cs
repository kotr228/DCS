namespace GrayCat.Core.Generation;

/// <summary>React Native (Expo) renderer for every block type. Reads the same site.json as the web outputs.</summary>
public static class ReactNativeLibrary
{
    public const string ExpoVersion = "~58.0.23";
    public const string ExpoStatusBarVersion = "~58.0.4";
    public const string ReactVersion = "19.3.0";
    public const string ReactNativeVersion = "0.88.1";
    public const string ReactNativeWebVersion = "~0.21.4";
    public const string MetroRuntimeVersion = "~58.0.17";

    public const string BlocksJs = """
import { useState } from 'react';
import {
  View, Text, Image, Pressable, TextInput, StyleSheet, Linking, ActivityIndicator,
} from 'react-native';
import { API_BASE_URL } from './config';
import localAssets from './assets';
import site from './data/site.json';

const colors = {
  primary: '#4f46e5', secondary: '#64748b', success: '#16a34a', danger: '#dc2626',
  warning: '#d97706', info: '#0891b2', text: '#1e293b', muted: '#64748b',
  surface: '#f8fafc', border: '#e2e8f0', dark: '#0f172a',
};

if (site.theme && site.theme.primary) colors.primary = site.theme.primary;

// Set by App.js: switches screens for links like "/about".
let navigate = null;
export function setNavigator(fn) {
  navigate = fn;
}

// Editor styles use web names; map the ones React Native understands.
function blockStyle(style = {}) {
  const out = {};
  if (style.background) out.backgroundColor = style.background;
  if (style.borderRadius) out.borderRadius = style.borderRadius;
  return out;
}

function textStyle(style = {}) {
  const out = {};
  if (style.color) out.color = style.color;
  if (style.fontSize) out.fontSize = style.fontSize;
  if (style.textAlign) out.textAlign = style.textAlign === 'justify' ? 'justify' : style.textAlign;
  return out;
}

// "assets/..." paths come from the project's assets folder; anything else is a web URL.
function img(src) {
  return localAssets[src] || { uri: src };
}

function open(url) {
  if (!url || url.startsWith('#')) return;
  if (url.startsWith('/') && navigate && navigate(url.split('#')[0] || '/')) return;
  Linking.openURL(url).catch(() => {});
}

function Button({ label, url, variant = 'primary', onPress, full, disabled }) {
  return (
    <Pressable
      onPress={onPress || (() => open(url))}
      disabled={disabled}
      style={({ pressed }) => [
        s.button,
        { backgroundColor: colors[variant] || colors.primary, opacity: disabled ? 0.5 : pressed ? 0.85 : 1 },
        full && { alignSelf: 'stretch' },
      ]}
    >
      <Text style={s.buttonText}>{label}</Text>
    </Pressable>
  );
}

function Section({ style, children, tone }) {
  return <View style={[s.section, tone === 'surface' && { backgroundColor: colors.surface }, blockStyle(style)]}>{children}</View>;
}

function TextBlock({ content, style }) {
  return <Section style={style}><Text style={[s.body, textStyle(style)]}>{content}</Text></Section>;
}

function HeaderBlock({ props, style }) {
  return (
    <Section style={style} tone="surface">
      <Text style={[s.h1, s.center, textStyle(style)]}>{props.title}</Text>
      {!!props.subtitle && <Text style={[s.lead, s.center]}>{props.subtitle}</Text>}
      {props.showNavigation && (
        <View style={s.rowCenter}>
          {(props.links || []).map((l, i) => (
            <Pressable key={i} onPress={() => open(l.url)}><Text style={s.link}>{l.text}</Text></Pressable>
          ))}
        </View>
      )}
    </Section>
  );
}

function NavbarBlock({ props, style }) {
  return (
    <View style={[s.navbar, blockStyle(style)]}>
      <View style={s.row}>
        {!!props.logo && <Image source={img(props.logo)} style={s.logo} />}
        <Text style={[s.brand, textStyle(style)]}>{props.brandName}</Text>
      </View>
      <View style={s.row}>
        {(props.menuItems || []).map((m, i) => (
          <Pressable key={i} onPress={() => open(m.url)}><Text style={[s.navLink, m.active && s.navLinkActive]}>{m.text}</Text></Pressable>
        ))}
      </View>
    </View>
  );
}

function HeroBlock({ props, style }) {
  const align = props.alignment === 'left' ? 'flex-start' : props.alignment === 'right' ? 'flex-end' : 'center';
  const minHeight = { small: 260, medium: 380, large: 500, full: 640 }[props.height] || 380;
  return (
    <View style={[s.hero, { minHeight, alignItems: align }, blockStyle(style)]}>
      {!!props.backgroundImage && <Image source={img(props.backgroundImage)} style={StyleSheet.absoluteFill} resizeMode="cover" />}
      {!!props.backgroundImage && props.overlay && <View style={[StyleSheet.absoluteFill, { backgroundColor: '#000', opacity: props.overlayOpacity }]} />}
      <Text style={[s.heroTitle, { textAlign: props.alignment }, textStyle(style)]}>{props.headline}</Text>
      {!!props.subheadline && <Text style={[s.heroLead, { textAlign: props.alignment }]}>{props.subheadline}</Text>}
      {!!props.ctaText && <Button label={props.ctaText} url={props.ctaUrl} variant="dark" />}
    </View>
  );
}

function ButtonBlock({ props, style }) {
  const align = style?.textAlign === 'center' ? 'center' : style?.textAlign === 'right' ? 'flex-end' : 'flex-start';
  return (
    <View style={[s.section, { alignItems: align }, blockStyle(style)]}>
      <Button label={props.text} url={props.url} variant={props.variant} full={props.fullWidth} disabled={props.disabled} />
    </View>
  );
}

function CardBlock({ props, style }) {
  return (
    <Section style={style}>
      <View style={[s.card, props.shadow && s.shadow, props.bordered && s.bordered]}>
        {!!props.image && <Image source={img(props.image)} style={s.cardImage} />}
        <View style={s.cardBody}>
          <Text style={[s.h3, textStyle(style)]}>{props.title}</Text>
          {!!props.description && <Text style={s.muted}>{props.description}</Text>}
          {!!props.linkText && <Pressable onPress={() => open(props.linkUrl)}><Text style={s.link}>{props.linkText} →</Text></Pressable>}
        </View>
      </View>
    </Section>
  );
}

function ImageBlock({ props, style }) {
  return (
    <Section style={style}>
      {props.src
        ? <Image source={img(props.src)} style={[s.image, style?.borderRadius ? { borderRadius: style.borderRadius } : null]} accessibilityLabel={props.alt} />
        : <Text style={[s.muted, s.center]}>No image selected</Text>}
      {!!props.caption && <Text style={[s.muted, s.center, { marginTop: 8 }]}>{props.caption}</Text>}
    </Section>
  );
}

function GalleryBlock({ props, style }) {
  return (
    <Section style={style}>
      {!!props.title && <Text style={[s.h2, s.center]}>{props.title}</Text>}
      <View style={s.grid}>
        {(props.images || []).map((photo, i) => (
          <Image key={i} source={img(photo.src)} style={s.gridImage} accessibilityLabel={photo.alt} />
        ))}
      </View>
    </Section>
  );
}

function ProductGridBlock({ props, style }) {
  return (
    <Section style={style}>
      {!!props.title && <Text style={[s.h2, s.center]}>{props.title}</Text>}
      {(props.products || []).map((p, i) => (
        <View key={i} style={[s.card, s.bordered, { marginBottom: 16 }]}>
          {!!p.image && <Image source={img(p.image)} style={s.cardImage} />}
          <View style={s.cardBody}>
            <Text style={s.h3}>{p.name}</Text>
            {!!p.description && <Text style={s.muted}>{p.description}</Text>}
            {!!p.price && <Text style={s.price}>{props.currency}{p.price}</Text>}
            {!!props.buttonText && <Button label={props.buttonText} url={p.url} />}
          </View>
        </View>
      ))}
    </Section>
  );
}

function FormBlock({ props, style }) {
  const fields = props.fields || [];
  const [values, setValues] = useState({});
  const [status, setStatus] = useState(null);
  const [sending, setSending] = useState(false);

  async function submit() {
    const missing = fields.find((f) => f.required && !String(values[f.name] || '').trim());
    if (missing) {
      setStatus({ ok: false, text: (props.requiredText || '{0} is required').replace('{0}', missing.label) });
      return;
    }
    setSending(true);
    setStatus(null);
    try {
      // On a device a relative URL has no host: set API_BASE_URL in src/config.js.
      const url = /^https?:/.test(props.submitUrl) ? props.submitUrl : `${API_BASE_URL}${props.submitUrl}`;
      const response = await fetch(url, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(values),
      });
      if (!response.ok) throw new Error(`HTTP ${response.status}`);
      setStatus({ ok: true, text: props.successMessage });
      setValues({});
    } catch (e) {
      setStatus({ ok: false, text: props.errorMessage });
    } finally {
      setSending(false);
    }
  }

  return (
    <Section style={style} tone="surface">
      {!!props.title && <Text style={[s.h2, s.center]}>{props.title}</Text>}
      {!!props.subtitle && <Text style={[s.muted, s.center, { marginBottom: 16 }]}>{props.subtitle}</Text>}
      {fields.map((f) => (
        <View key={f.name} style={{ marginBottom: 12 }}>
          <Text style={s.label}>{f.label}{f.required ? ' *' : ''}</Text>
          <TextInput
            style={[s.input, f.type === 'textarea' && { height: 110, textAlignVertical: 'top' }]}
            multiline={f.type === 'textarea'}
            keyboardType={f.type === 'email' ? 'email-address' : f.type === 'tel' ? 'phone-pad' : f.type === 'number' ? 'numeric' : 'default'}
            autoCapitalize={f.type === 'email' ? 'none' : 'sentences'}
            secureTextEntry={f.type === 'password'}
            placeholder={f.placeholder}
            value={values[f.name] || ''}
            onChangeText={(text) => setValues((v) => ({ ...v, [f.name]: text }))}
          />
        </View>
      ))}
      {sending ? <ActivityIndicator color={colors.primary} /> : <Button label={props.submitText} onPress={submit} full />}
      {status && <Text style={[s.status, { color: status.ok ? colors.success : colors.danger }]}>{status.text}</Text>}
    </Section>
  );
}

function VideoBlock({ props, style }) {
  return (
    <Section style={style}>
      {props.videoUrl
        ? <Button label="▶  Watch video" url={props.videoUrl} variant="dark" />
        : <Text style={[s.muted, s.center]}>Add a video URL in GrayCat Studio</Text>}
    </Section>
  );
}

function TestimonialBlock({ props, style }) {
  return (
    <Section style={style}>
      <Text style={[s.quote, textStyle(style)]}>“{props.quote}”</Text>
      {props.showRating && <Text style={s.stars}>{'★'.repeat(props.rating)}{'☆'.repeat(5 - props.rating)}</Text>}
      <View style={s.rowCenter}>
        {!!props.avatar && <Image source={img(props.avatar)} style={s.avatar} />}
        <View>
          <Text style={s.bold}>{props.author}</Text>
          {!!props.role && <Text style={s.muted}>{props.role}</Text>}
        </View>
      </View>
    </Section>
  );
}

function PricingBlock({ props, style }) {
  return (
    <Section style={style}>
      <View style={[s.card, s.bordered, s.priceCard, props.highlighted && { borderColor: colors.primary, borderWidth: 2 }]}>
        {!!props.badge && <Text style={s.badge}>{props.badge}</Text>}
        <Text style={[s.h3, s.center]}>{props.planName}</Text>
        <Text style={s.amount}>{props.currency}{props.price}{!!props.period && <Text style={s.muted}>/{props.period}</Text>}</Text>
        {(props.features || []).map((f, i) => <Text key={i} style={s.feature}>✓  {f}</Text>)}
        {!!props.ctaText && <View style={{ marginTop: 16 }}><Button label={props.ctaText} url={props.ctaUrl} full /></View>}
      </View>
    </Section>
  );
}

function AccordionBlock({ props, style }) {
  const [open, setOpen] = useState(() => new Set([props.defaultOpen]));
  function toggle(i) {
    setOpen((current) => {
      const next = new Set(props.allowMultiple ? current : []);
      if (current.has(i)) next.delete(i); else next.add(i);
      return next;
    });
  }
  return (
    <Section style={style}>
      {!!props.title && <Text style={[s.h2, s.center]}>{props.title}</Text>}
      {(props.items || []).map((item, i) => (
        <View key={i} style={s.accordionItem}>
          <Pressable onPress={() => toggle(i)} style={s.accordionHead}>
            <Text style={[s.bold, { flex: 1 }]}>{item.question}</Text>
            <Text style={s.link}>{open.has(i) ? '−' : '+'}</Text>
          </Pressable>
          {open.has(i) && <Text style={[s.muted, { padding: 14, paddingTop: 0 }]}>{item.answer}</Text>}
        </View>
      ))}
    </Section>
  );
}

function StatsBlock({ props, style }) {
  const vertical = props.layout === 'vertical';
  return (
    <Section style={style} tone="surface">
      <View style={vertical ? null : s.statsRow}>
        {(props.stats || []).map((st, i) => (
          <View key={i} style={s.stat}>
            <Text style={s.statNumber}>{st.number}</Text>
            <Text style={s.muted}>{st.label}</Text>
          </View>
        ))}
      </View>
    </Section>
  );
}

function SocialLinksBlock({ props, style }) {
  const justify = props.alignment === 'left' ? 'flex-start' : props.alignment === 'right' ? 'flex-end' : 'center';
  return (
    <Section style={style}>
      <View style={[s.row, { justifyContent: justify }]}>
        {(props.links || []).map((l, i) => (
          <Pressable key={i} onPress={() => open(l.url)} style={props.style === 'text' ? null : s.socialIcon} accessibilityLabel={l.label}>
            <Text style={props.style === 'text' ? s.link : s.socialText}>{props.style === 'text' ? l.label : l.initial}</Text>
          </Pressable>
        ))}
      </View>
    </Section>
  );
}

function DividerBlock({ props, style }) {
  const spacing = { small: 8, medium: 24, large: 48 }[props.spacing] || 24;
  return (
    <View style={[{ paddingVertical: spacing, paddingHorizontal: 20 }, blockStyle(style)]}>
      <View style={{ borderTopWidth: props.thickness, borderTopColor: props.color, borderStyle: props.style === 'double' ? 'solid' : props.style }} />
    </View>
  );
}

function FooterBlock({ props, style }) {
  return (
    <View style={[s.footer, blockStyle(style)]}>
      <Text style={s.footerText}>{props.copyright}</Text>
      <View style={s.rowCenter}>
        {(props.links || []).map((l, i) => (
          <Pressable key={i} onPress={() => open(l.url)}><Text style={s.footerLink}>{l.text}</Text></Pressable>
        ))}
      </View>
    </View>
  );
}

const registry = {
  Text: TextBlock, Header: HeaderBlock, Navbar: NavbarBlock, Hero: HeroBlock, Button: ButtonBlock,
  Card: CardBlock, Image: ImageBlock, Gallery: GalleryBlock, ProductGrid: ProductGridBlock,
  Form: FormBlock, ContactForm: FormBlock, Video: VideoBlock, Testimonial: TestimonialBlock,
  Pricing: PricingBlock, Accordion: AccordionBlock, Stats: StatsBlock, SocialLinks: SocialLinksBlock,
  Divider: DividerBlock, Footer: FooterBlock,
};

export function Block({ block }) {
  const Component = registry[block.type] || TextBlock;
  return <Component {...block} />;
}

const s = StyleSheet.create({
  section: { paddingVertical: 28, paddingHorizontal: 20 },
  center: { textAlign: 'center' },
  row: { flexDirection: 'row', alignItems: 'center', flexWrap: 'wrap', gap: 12 },
  rowCenter: { flexDirection: 'row', alignItems: 'center', justifyContent: 'center', flexWrap: 'wrap', gap: 16, marginTop: 12 },
  body: { fontSize: 16, lineHeight: 24, color: colors.text },
  h1: { fontSize: 30, fontWeight: '800', color: colors.text, marginBottom: 8 },
  h2: { fontSize: 24, fontWeight: '800', color: colors.text, marginBottom: 16 },
  h3: { fontSize: 19, fontWeight: '700', color: colors.text, marginBottom: 6 },
  lead: { fontSize: 17, color: colors.muted },
  muted: { color: colors.muted, fontSize: 15, lineHeight: 22 },
  bold: { fontWeight: '700', color: colors.text },
  link: { color: colors.primary, fontWeight: '600', fontSize: 16 },
  navbar: { paddingTop: 16, paddingBottom: 12, paddingHorizontal: 20, backgroundColor: '#fff', borderBottomWidth: 1, borderBottomColor: colors.border, gap: 8 },
  brand: { fontSize: 20, fontWeight: '800', color: colors.text },
  logo: { width: 28, height: 28 },
  navLink: { color: colors.muted, fontWeight: '600' },
  navLinkActive: { color: colors.primary },
  hero: { justifyContent: 'center', padding: 28, backgroundColor: colors.primary, overflow: 'hidden' },
  heroTitle: { color: '#fff', fontSize: 34, fontWeight: '800', marginBottom: 12 },
  heroLead: { color: '#e0e7ff', fontSize: 18, marginBottom: 20 },
  button: { paddingVertical: 13, paddingHorizontal: 22, borderRadius: 10, alignSelf: 'flex-start', alignItems: 'center' },
  buttonText: { color: '#fff', fontWeight: '700', fontSize: 16 },
  card: { backgroundColor: '#fff', borderRadius: 12, overflow: 'hidden' },
  shadow: { shadowColor: '#000', shadowOpacity: 0.1, shadowRadius: 12, shadowOffset: { width: 0, height: 6 }, elevation: 4 },
  bordered: { borderWidth: 1, borderColor: colors.border },
  cardImage: { width: '100%', height: 180 },
  cardBody: { padding: 18, gap: 6 },
  image: { width: '100%', aspectRatio: 16 / 10, borderRadius: 8 },
  grid: { flexDirection: 'row', flexWrap: 'wrap', gap: 8 },
  gridImage: { width: '48%', aspectRatio: 3 / 2, borderRadius: 8 },
  price: { fontSize: 20, fontWeight: '800', color: colors.primary, marginVertical: 6 },
  label: { fontWeight: '600', marginBottom: 6, color: colors.text },
  input: { borderWidth: 1, borderColor: colors.border, borderRadius: 8, padding: 12, backgroundColor: '#fff', fontSize: 16 },
  status: { marginTop: 12, fontWeight: '600', textAlign: 'center' },
  quote: { fontSize: 20, fontStyle: 'italic', textAlign: 'center', color: colors.text, marginBottom: 12 },
  stars: { color: '#f59e0b', fontSize: 20, textAlign: 'center', letterSpacing: 2 },
  avatar: { width: 48, height: 48, borderRadius: 24 },
  priceCard: { padding: 24 },
  badge: { alignSelf: 'center', backgroundColor: colors.primary, color: '#fff', fontWeight: '700', paddingHorizontal: 12, paddingVertical: 4, borderRadius: 999, overflow: 'hidden', marginBottom: 10 },
  amount: { fontSize: 40, fontWeight: '800', textAlign: 'center', color: colors.text, marginVertical: 12 },
  feature: { paddingVertical: 8, borderBottomWidth: 1, borderBottomColor: colors.border, color: colors.text },
  accordionItem: { borderWidth: 1, borderColor: colors.border, borderRadius: 10, marginBottom: 10, backgroundColor: '#fff' },
  accordionHead: { flexDirection: 'row', alignItems: 'center', padding: 14, gap: 12 },
  statsRow: { flexDirection: 'row', justifyContent: 'space-around', flexWrap: 'wrap', gap: 16 },
  stat: { alignItems: 'center', marginVertical: 8 },
  statNumber: { fontSize: 30, fontWeight: '800', color: colors.primary },
  socialIcon: { width: 42, height: 42, borderRadius: 21, backgroundColor: colors.primary, alignItems: 'center', justifyContent: 'center' },
  socialText: { color: '#fff', fontWeight: '800' },
  footer: { backgroundColor: colors.dark, padding: 24, alignItems: 'center' },
  footerText: { color: '#cbd5e1', textAlign: 'center' },
  footerLink: { color: '#e2e8f0' },
});
""";

    public const string AppJs = """
import { useEffect, useRef, useState } from 'react';
import { StatusBar } from 'expo-status-bar';
import { BackHandler, ScrollView, StyleSheet, Text, View, Platform } from 'react-native';
import site from './src/data/site.json';
import { Block, setNavigator } from './src/blocks';

export default function App() {
  const [page, setPage] = useState(site.pages[0]);
  const scroll = useRef(null);

  // Links like "/about" in navigation bars and buttons switch screens.
  useEffect(() => {
    setNavigator((path) => {
      const target = site.pages.find((p) => p.path === path);
      if (!target) return false;
      setPage(target);
      scroll.current?.scrollTo({ y: 0, animated: false });
      return true;
    });
  }, []);

  // Android back button returns to the home screen.
  useEffect(() => {
    const subscription = BackHandler.addEventListener('hardwareBackPress', () => {
      if (page.path === '/') return false;
      setPage(site.pages[0]);
      return true;
    });
    return () => subscription.remove();
  }, [page]);

  return (
    <View style={styles.safe}>
      <ScrollView ref={scroll} contentContainerStyle={styles.content}>
        {page.blocks.length === 0 ? (
          <View style={styles.empty}>
            <Text style={styles.emptyTitle}>Your app is empty</Text>
            <Text style={styles.emptyText}>Add blocks in GrayCat Studio to build it.</Text>
          </View>
        ) : (
          page.blocks.map((block) => <Block key={block.id} block={block} />)
        )}
      </ScrollView>
      <StatusBar style="auto" />
    </View>
  );
}

const styles = StyleSheet.create({
  safe: { flex: 1, backgroundColor: '#fff', paddingTop: Platform.OS === 'web' ? 0 : 48 },
  content: { flexGrow: 1 },
  empty: { flex: 1, alignItems: 'center', justifyContent: 'center', padding: 32 },
  emptyTitle: { fontSize: 22, fontWeight: '700', marginBottom: 8 },
  emptyText: { color: '#64748b', textAlign: 'center' },
});
""";

    public const string ConfigJs = """
// Where forms send their data when the app runs on a phone.
// Point this at your API, e.g. the GrayCat "NodeJsApi" project: 'http://192.168.1.10:3000'
export const API_BASE_URL = 'http://localhost:3000';
""";
}
