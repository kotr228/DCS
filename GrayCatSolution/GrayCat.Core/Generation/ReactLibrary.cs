namespace GrayCat.Core.Generation;

/// <summary>
/// React components for every block type. Used as-is by the React (Vite) and Next.js outputs.
/// Each component receives one site.json block: { id, type, content, style, props }.
/// </summary>
public static class ReactLibrary
{
    public static IReadOnlyDictionary<string, string> Components { get; } = new Dictionary<string, string>
    {
        ["index.jsx"] = """
import TextBlock from './TextBlock';
import HeaderBlock from './HeaderBlock';
import NavbarBlock from './NavbarBlock';
import HeroBlock from './HeroBlock';
import ButtonBlock from './ButtonBlock';
import CardBlock from './CardBlock';
import ImageBlock from './ImageBlock';
import GalleryBlock from './GalleryBlock';
import ProductGridBlock from './ProductGridBlock';
import FormBlock from './FormBlock';
import VideoBlock from './VideoBlock';
import TestimonialBlock from './TestimonialBlock';
import PricingBlock from './PricingBlock';
import AccordionBlock from './AccordionBlock';
import StatsBlock from './StatsBlock';
import SocialLinksBlock from './SocialLinksBlock';
import DividerBlock from './DividerBlock';
import FooterBlock from './FooterBlock';

const registry = {
  Text: TextBlock,
  Header: HeaderBlock,
  Navbar: NavbarBlock,
  Hero: HeroBlock,
  Button: ButtonBlock,
  Card: CardBlock,
  Image: ImageBlock,
  Gallery: GalleryBlock,
  ProductGrid: ProductGridBlock,
  Form: FormBlock,
  ContactForm: FormBlock,
  Video: VideoBlock,
  Testimonial: TestimonialBlock,
  Pricing: PricingBlock,
  Accordion: AccordionBlock,
  Stats: StatsBlock,
  SocialLinks: SocialLinksBlock,
  Divider: DividerBlock,
  Footer: FooterBlock,
};

export function Block({ block }) {
  const Component = registry[block.type] || TextBlock;
  return <Component {...block} />;
}

export function Page({ page }) {
  if (!page || !page.blocks || page.blocks.length === 0) {
    return (
      <div className="gc-empty">
        <h1>This page is empty</h1>
        <p>Add blocks in GrayCat Studio to build it.</p>
      </div>
    );
  }
  // Cards that follow each other sit side by side.
  const parts = [];
  page.blocks.forEach((block) => {
    const last = parts[parts.length - 1];
    if (block.type === 'Card' && Array.isArray(last)) last.push(block);
    else parts.push(block.type === 'Card' ? [block] : block);
  });
  return parts.map((part) =>
    Array.isArray(part) ? (
      part.length > 1 ? (
        <div className="gc-card-row" key={part[0].id}>
          {part.map((block) => <Block key={block.id} block={block} />)}
        </div>
      ) : (
        <Block key={part[0].id} block={part[0]} />
      )
    ) : (
      <Block key={part.id} block={part} />
    )
  );
}

/** Site colours and font as CSS variables (see graycat.css). */
export function themeStyle(theme) {
  if (!theme) return undefined;
  return {
    '--gc-primary': theme.primary,
    '--gc-primary-dark': theme.primaryDark,
    '--gc-accent': theme.accent,
    '--gc-font': theme.fontStack,
  };
}
""",

        ["TextBlock.jsx"] = """
export default function TextBlock({ anchor, content, style }) {
  return (
    <section id={anchor || undefined} className="gc-block gc-text" style={style}>
      <div className="gc-container">
        <p>{content}</p>
      </div>
    </section>
  );
}
""",

        ["HeaderBlock.jsx"] = """
export default function HeaderBlock({ anchor, props, style }) {
  const { title, subtitle, showNavigation, links = [] } = props;
  return (
    <header id={anchor || undefined} className="gc-block gc-header" style={style}>
      <div className="gc-container">
        <h1>{title}</h1>
        {subtitle && <p className="gc-lead">{subtitle}</p>}
        {showNavigation && links.length > 0 && (
          <nav className="gc-header-nav">
            {links.map((link, i) => (
              <a key={i} href={link.url} className={link.active ? 'is-active' : undefined} aria-current={link.active ? 'page' : undefined}>{link.text}</a>
            ))}
          </nav>
        )}
      </div>
    </header>
  );
}
""",

        ["NavbarBlock.jsx"] = """
export default function NavbarBlock({ anchor, props, style }) {
  const { brandName, logo, position, transparent, showSearch, ctaText, ctaUrl, homeUrl, menuItems = [] } = props;
  let className = 'gc-block gc-navbar';
  if (position && position !== 'static') className += ` gc-navbar-${position}`;
  if (transparent) className += ' gc-navbar-transparent';
  return (
    <nav id={anchor || undefined} className={className} style={style}>
      <div className="gc-container gc-navbar-inner">
        <a className="gc-brand" href={homeUrl || '#'}>
          {logo && <img src={logo} alt="" />}
          {brandName}
        </a>
        <ul className="gc-nav-links">
          {menuItems.map((item, i) => (
            <li key={i}>
              <a href={item.url} className={item.active ? 'is-active' : undefined} aria-current={item.active ? 'page' : undefined}>{item.text}</a>
            </li>
          ))}
        </ul>
        {showSearch && <input className="gc-search" type="search" placeholder="Search" aria-label="Search" />}
        {ctaText && <a className="gc-btn gc-btn-primary gc-btn-small" href={ctaUrl}>{ctaText}</a>}
      </div>
    </nav>
  );
}
""",

        ["HeroBlock.jsx"] = """
export default function HeroBlock({ anchor, props, style }) {
  const { headline, subheadline, ctaText, ctaUrl, backgroundImage, overlay, overlayOpacity, alignment, height } = props;
  const heroStyle = { ...style };
  if (backgroundImage) heroStyle.backgroundImage = `url("${backgroundImage}")`;
  return (
    <section id={anchor || undefined} className={`gc-block gc-hero gc-hero-${height} gc-align-${alignment}`} style={heroStyle}>
      {overlay && backgroundImage && <div className="gc-hero-overlay" style={{ opacity: overlayOpacity }} />}
      <div className="gc-container gc-hero-content">
        <h1>{headline}</h1>
        {subheadline && <p className="gc-lead">{subheadline}</p>}
        {ctaText && <a className="gc-btn gc-btn-large" href={ctaUrl}>{ctaText}</a>}
      </div>
    </section>
  );
}
""",

        ["ButtonBlock.jsx"] = """
export default function ButtonBlock({ anchor, props, style }) {
  const { text, url, variant, size, fullWidth, disabled, target } = props;
  let className = `gc-btn gc-btn-${variant} gc-btn-${size}`;
  if (fullWidth) className += ' gc-btn-block';
  if (disabled) className += ' is-disabled';
  const external = target === '_blank' ? { target: '_blank', rel: 'noopener noreferrer' } : {};
  return (
    <div id={anchor || undefined} className="gc-block gc-button-block" style={style}>
      <div className="gc-container">
        <a className={className} href={url} aria-disabled={disabled || undefined} tabIndex={disabled ? -1 : undefined} {...external}>
          {text}
        </a>
      </div>
    </div>
  );
}
""",

        ["CardBlock.jsx"] = """
export default function CardBlock({ anchor, props, style }) {
  const { title, description, image, imagePosition, linkText, linkUrl, shadow, bordered, hoverable } = props;
  let className = `gc-card gc-card-img-${imagePosition}`;
  if (shadow) className += ' gc-card-shadow';
  if (bordered) className += ' gc-card-bordered';
  if (hoverable) className += ' gc-card-hover';
  return (
    <section id={anchor || undefined} className="gc-block gc-card-block" style={style}>
      <div className="gc-container">
        <article className={className}>
          {image && <img className="gc-card-img" src={image} alt={title} loading="lazy" />}
          <div className="gc-card-body">
            <h3>{title}</h3>
            {description && <p>{description}</p>}
            {linkText && <a className="gc-link" href={linkUrl}>{linkText} →</a>}
          </div>
        </article>
      </div>
    </section>
  );
}
""",

        ["ImageBlock.jsx"] = """
export default function ImageBlock({ anchor, props, style }) {
  const { src, alt, caption } = props;
  return (
    <figure id={anchor || undefined} className="gc-block gc-image" style={style}>
      {src ? <img src={src} alt={alt} loading="lazy" /> : <div className="gc-placeholder">No image selected</div>}
      {caption && <figcaption>{caption}</figcaption>}
    </figure>
  );
}
""",

        ["GalleryBlock.jsx"] = """
export default function GalleryBlock({ anchor, props, style }) {
  const { title, columns, images = [] } = props;
  return (
    <section id={anchor || undefined} className="gc-block gc-gallery" style={style}>
      <div className="gc-container">
        {title && <h2 className="gc-section-title">{title}</h2>}
        <div className="gc-grid" style={{ '--gc-cols': columns }}>
          {images.map((image, i) => (
            <figure key={i}>
              <img src={image.src} alt={image.alt} loading="lazy" />
              {image.caption && <figcaption>{image.caption}</figcaption>}
            </figure>
          ))}
        </div>
      </div>
    </section>
  );
}
""",

        ["ProductGridBlock.jsx"] = """
export default function ProductGridBlock({ anchor, props, style }) {
  const { title, currency, columns, buttonText, products = [] } = props;
  return (
    <section id={anchor || undefined} className="gc-block gc-productgrid" style={style}>
      <div className="gc-container">
        {title && <h2 className="gc-section-title">{title}</h2>}
        <div className="gc-grid" style={{ '--gc-cols': columns }}>
          {products.map((product, i) => (
            <article className="gc-product" key={i}>
              {product.image && <img src={product.image} alt={product.name} loading="lazy" />}
              <div className="gc-product-body">
                <h3>{product.name}</h3>
                {product.description && <p>{product.description}</p>}
                {product.price && <div className="gc-product-price">{currency}{product.price}</div>}
                {buttonText && (
                  <a className="gc-btn gc-btn-primary gc-btn-small" href={product.url || '#'}>{buttonText}</a>
                )}
              </div>
            </article>
          ))}
        </div>
      </div>
    </section>
  );
}
""",

        ["FormBlock.jsx"] = """
import { useState } from 'react';

export default function FormBlock({ anchor, id, type, props, style }) {
  const { title, subtitle, submitText, submitUrl, successMessage, errorMessage, sendingText, fields = [] } = props;
  const [status, setStatus] = useState({ kind: '', text: '' });
  const [sending, setSending] = useState(false);

  async function handleSubmit(event) {
    event.preventDefault();
    const form = event.currentTarget;
    const data = Object.fromEntries(new FormData(form).entries());
    setSending(true);
    setStatus({ kind: '', text: sendingText || 'Sending...' });
    try {
      const response = await fetch(submitUrl, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(data),
      });
      if (!response.ok) throw new Error(`HTTP ${response.status}`);
      setStatus({ kind: 'is-success', text: successMessage });
      form.reset();
    } catch (error) {
      setStatus({ kind: 'is-error', text: errorMessage });
    } finally {
      setSending(false);
    }
  }

  return (
    <section id={anchor || undefined} className={`gc-block gc-form-block gc-${type.toLowerCase()}`} style={style}>
      <div className="gc-container gc-narrow">
        {title && <h2>{title}</h2>}
        {subtitle && <p>{subtitle}</p>}
        <form className="gc-form" onSubmit={handleSubmit}>
          {fields.map((field) => {
            const fieldId = `${id}-${field.name}`;
            return (
              <div className="gc-field" key={field.name}>
                <label htmlFor={fieldId}>
                  {field.label}
                  {field.required && <span className="gc-required"> *</span>}
                </label>
                {field.type === 'textarea' ? (
                  <textarea id={fieldId} name={field.name} placeholder={field.placeholder || undefined} required={field.required} />
                ) : (
                  <input id={fieldId} name={field.name} type={field.type} placeholder={field.placeholder || undefined} required={field.required} />
                )}
              </div>
            );
          })}
          <button type="submit" className="gc-btn gc-btn-primary" disabled={sending}>{submitText}</button>
          <p className={`gc-form-status ${status.kind}`} role="status" aria-live="polite">{status.text}</p>
        </form>
      </div>
    </section>
  );
}
""",

        ["VideoBlock.jsx"] = """
export default function VideoBlock({ anchor, props, style }) {
  const { embedUrl, isFile, aspectRatio, autoplay, controls, loop, muted } = props;
  let inner;
  if (!embedUrl) {
    inner = <div className="gc-placeholder">Add a video URL in GrayCat Studio</div>;
  } else if (isFile) {
    inner = (
      <div className="gc-video-frame" style={{ aspectRatio }}>
        <video src={embedUrl} controls={controls} autoPlay={autoplay} loop={loop} muted={muted} playsInline />
      </div>
    );
  } else {
    inner = (
      <div className="gc-video-frame" style={{ aspectRatio }}>
        <iframe
          src={embedUrl}
          title="Video"
          referrerPolicy="strict-origin-when-cross-origin"
          allow="accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture"
          allowFullScreen
        />
      </div>
    );
  }
  return (
    <section id={anchor || undefined} className="gc-block gc-video" style={style}>
      <div className="gc-container">{inner}</div>
    </section>
  );
}
""",

        ["TestimonialBlock.jsx"] = """
export default function TestimonialBlock({ anchor, props, style }) {
  const { quote, author, role, avatar, rating, showRating, layout } = props;
  return (
    <section id={anchor || undefined} className={`gc-block gc-testimonial gc-testimonial-${layout}`} style={style}>
      <div className="gc-container gc-narrow">
        <blockquote>
          <p className="gc-quote">“{quote}”</p>
          {showRating && (
            <div className="gc-stars" aria-label={`${rating} out of 5`}>
              {'★'.repeat(rating)}{'☆'.repeat(5 - rating)}
            </div>
          )}
          <footer className="gc-author">
            {avatar && <img src={avatar} alt={author} />}
            <div>
              <strong>{author}</strong>
              {role && <span>{role}</span>}
            </div>
          </footer>
        </blockquote>
      </div>
    </section>
  );
}
""",

        ["PricingBlock.jsx"] = """
export default function PricingBlock({ anchor, props, style }) {
  const { planName, price, currency, period, features = [], ctaText, ctaUrl, highlighted, badge } = props;
  return (
    <section id={anchor || undefined} className="gc-block gc-pricing" style={style}>
      <div className="gc-container">
        <div className={`gc-price-card${highlighted ? ' gc-price-highlight' : ''}`}>
          {badge && <span className="gc-badge">{badge}</span>}
          <h3>{planName}</h3>
          <div className="gc-price">
            <span className="gc-currency">{currency}</span>
            <span className="gc-amount">{price}</span>
            {period && <span className="gc-period">/{period}</span>}
          </div>
          <ul className="gc-features">
            {features.map((feature, i) => <li key={i}>{feature}</li>)}
          </ul>
          {ctaText && <a className="gc-btn gc-btn-primary gc-btn-block" href={ctaUrl}>{ctaText}</a>}
        </div>
      </div>
    </section>
  );
}
""",

        ["AccordionBlock.jsx"] = """
export default function AccordionBlock({ anchor, id, props, style }) {
  const { title, items = [], allowMultiple, defaultOpen } = props;
  const groupName = allowMultiple ? undefined : `acc-${id}`;
  return (
    <section id={anchor || undefined} className="gc-block gc-accordion" style={style}>
      <div className="gc-container gc-narrow">
        {title && <h2>{title}</h2>}
        <div className="gc-accordion-list">
          {items.map((item, i) => (
            <details className="gc-accordion-item" name={groupName} open={i === defaultOpen} key={i}>
              <summary>{item.question}</summary>
              <div className="gc-accordion-body">{item.answer}</div>
            </details>
          ))}
        </div>
      </div>
    </section>
  );
}
""",

        ["StatsBlock.jsx"] = """
export default function StatsBlock({ anchor, props, style }) {
  const { stats = [], layout } = props;
  return (
    <section id={anchor || undefined} className={`gc-block gc-stats gc-stats-${layout}`} style={style}>
      <div className="gc-container">
        <div className="gc-stats-row">
          {stats.map((stat, i) => (
            <div className="gc-stat" key={i}>
              <div className="gc-stat-number">{stat.number}</div>
              <div className="gc-stat-label">{stat.label}</div>
            </div>
          ))}
        </div>
      </div>
    </section>
  );
}
""",

        ["SocialLinksBlock.jsx"] = """
export default function SocialLinksBlock({ anchor, props, style }) {
  const { links = [], style: look, size, alignment } = props;
  return (
    <section id={anchor || undefined} className={`gc-block gc-sociallinks gc-social gc-social-${look} gc-social-${size} gc-align-${alignment}`} style={style}>
      <div className="gc-container">
        <ul className="gc-social-list">
          {links.map((link, i) => (
            <li key={i}>
              <a className="gc-social-link" href={link.url} target="_blank" rel="noopener noreferrer" aria-label={link.label}>
                <span className="gc-social-icon" aria-hidden="true">{link.initial}</span>
                <span className="gc-social-name">{link.label}</span>
              </a>
            </li>
          ))}
        </ul>
      </div>
    </section>
  );
}
""",

        ["DividerBlock.jsx"] = """
export default function DividerBlock({ anchor, props, style }) {
  const { style: lineStyle, thickness, color, spacing, withIcon, icon } = props;
  const line = <hr style={{ borderTop: `${thickness}px ${lineStyle} ${color}` }} />;
  return (
    <div id={anchor || undefined} className={`gc-block gc-divider gc-divider-${spacing}`} style={style}>
      <div className="gc-container">
        {withIcon && icon ? (
          <div className="gc-divider-with-icon">
            {line}
            <span className="gc-divider-icon">{icon}</span>
            <hr style={{ borderTop: `${thickness}px ${lineStyle} ${color}` }} />
          </div>
        ) : line}
      </div>
    </div>
  );
}
""",

        ["FooterBlock.jsx"] = """
export default function FooterBlock({ anchor, props, style }) {
  const { copyright, links = [] } = props;
  return (
    <footer id={anchor || undefined} className="gc-block gc-footer" style={style}>
      <div className="gc-container gc-footer-inner">
        <p>{copyright}</p>
        {links.length > 0 && (
          <nav>
            {links.map((link, i) => <a key={i} href={link.url}>{link.text}</a>)}
          </nav>
        )}
      </div>
    </footer>
  );
}
""",
    };
}
