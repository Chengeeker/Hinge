import 'package:flutter/material.dart';

/// Shared page shell for every Android workspace destination. Keeping the
/// scroll, width and bottom-navigation inset here prevents individual pages
/// from drifting apart as they evolve.
class HingePageBody extends StatelessWidget {
  final Widget child;
  final bool reserveFloatingNavigation;

  const HingePageBody({
    super.key,
    required this.child,
    this.reserveFloatingNavigation = false,
  });

  @override
  Widget build(BuildContext context) {
    final bottomPadding = reserveFloatingNavigation
        ? 136 + MediaQuery.viewPaddingOf(context).bottom
        : 32.0;
    return SingleChildScrollView(
      padding: EdgeInsets.fromLTRB(24, 8, 24, bottomPadding),
      child: Center(
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 1180),
          child: child,
        ),
      ),
    );
  }
}

class HingeSectionTitle extends StatelessWidget {
  final String title;
  final String subtitle;

  const HingeSectionTitle({
    super.key,
    required this.title,
    required this.subtitle,
  });

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(title, style: Theme.of(context).textTheme.headlineSmall),
        const SizedBox(height: 4),
        Text(
          subtitle,
          style: TextStyle(
            color: Theme.of(context).colorScheme.onSurfaceVariant,
          ),
        ),
      ],
    );
  }
}
