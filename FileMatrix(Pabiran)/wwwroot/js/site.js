/**
 * FileMatrix Global Client Logic (site.js)
 * 
 * RESPONSIBILITY: Orchestrates landing page interactivity, smooth navigation, 
 * and handles global modal triggers (specifically the dynamic Authentication Modal).
 */
// Minimal interactivity for placeholder UI
document.addEventListener('DOMContentLoaded', function () {
    // Landing page section IDs for scroll-spy and navigation
    const sectionIds = ['features', 'benefits', 'how-it-works'];
    const navLinks = {};
    sectionIds.forEach(function (id) {
        navLinks[id] = document.querySelectorAll('a[href="#' + id + '"]');
    });

    // NAVIGATION: The 'Smooth Anchor' pattern. 
    // Prevents abrupt jumps by calculating navigation offsets, 
    // taking the fixed header into account.
    var navOffset = 80; // fixed nav header height
    document.querySelectorAll('a[href^="#"]').forEach(function (anchor) {
        anchor.addEventListener('click', function (e) {
            var href = this.getAttribute('href');
            // Skip bare "#" links (used for modals / placeholders)
            if (!href || href === '#') return;
            var target = document.querySelector(href);
            if (target) {
                e.preventDefault();
                var top = target.getBoundingClientRect().top + window.pageYOffset - navOffset;
                window.scrollTo({ top: top, behavior: 'smooth' });
                // Update URL hash without jumping
                history.pushState(null, '', href);
            }
        });
    });

    // SCROLL-SPY: The 'Active Observer' pattern. 
    // Highlights the current landing page section in the navbar 
    // based on the user's scroll position.
    function updateActiveNav() {
        var scrollPos = window.scrollY + navOffset + 40;
        var currentId = '';
        sectionIds.forEach(function (id) {
            var section = document.getElementById(id);
            if (section && section.offsetTop <= scrollPos) {
                currentId = id;
            }
        });
        // Clear all, then set active
        Object.keys(navLinks).forEach(function (id) {
            (navLinks[id] || []).forEach(function (a) {
                a.classList.remove('active-section');
            });
        });
        if (currentId && navLinks[currentId]) {
            navLinks[currentId].forEach(function (a) {
                a.classList.add('active-section');
            });
        }
    }

    window.addEventListener('scroll', updateActiveNav, { passive: true });
    updateActiveNav();

    // If a nav link opens the auth modal, switch to requested tab
    var authModal = document.getElementById('authModal');
    if (authModal) {
        authModal.addEventListener('show.bs.modal', function (event) {
            var trigger = event.relatedTarget;
            if (!trigger) return;
            var tab = trigger.getAttribute('data-auth-tab');
            if (!tab) return;
            var tabTriggerEl = document.querySelector('#' + tab + '-tab');
            if (tabTriggerEl) {
                var tabInstance = new bootstrap.Tab(tabTriggerEl);
                tabInstance.show();
            }
        });
    }

    // ROUTING: The 'Hash-to-Modal' pattern. 
    // Allows deep-linking directly into the Register or Login tabs 
    // of the authentication modal (e.g., via ?auth=register).
    try {
        var urlParams = new URLSearchParams(window.location.search);
        var authParam = urlParams.get('auth');
        if (!authParam && window.location.hash) {
            authParam = window.location.hash.replace('#', '');
        }
        if (authParam && authModal && window.bootstrap) {
            var tabTriggerEl = document.querySelector('#' + authParam + '-tab');
            if (tabTriggerEl) {
                var tabInstance = new bootstrap.Tab(tabTriggerEl);
                // show modal first then switch tab
                var modal = new bootstrap.Modal(authModal);
                modal.show();
                tabInstance.show();
            }
        }
    } catch (e) { console.error(e); }

    // Reveal-on-scroll animations for landing page
    var revealElements = document.querySelectorAll('.reveal-on-scroll');
    if (revealElements.length) {
        if ('IntersectionObserver' in window) {
            var observer = new IntersectionObserver(function (entries, obs) {
                entries.forEach(function (entry) {
                    if (entry.isIntersecting) {
                        entry.target.classList.add('is-visible');
                        obs.unobserve(entry.target);
                    }
                });
            }, { threshold: 0.15 });

            revealElements.forEach(function (el) {
                observer.observe(el);
            });
        } else {
            // Fallback: show all immediately
            revealElements.forEach(function (el) {
                el.classList.add('is-visible');
            });
        }
    }
});
