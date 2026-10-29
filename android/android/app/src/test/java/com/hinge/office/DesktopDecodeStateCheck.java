package com.hinge.office;

import java.io.IOException;

/** Plain JVM self-check; no phone, emulator, Android stubs or JUnit required. */
public final class DesktopDecodeStateCheck {
    private interface Checked { void run() throws IOException; }
    private static void rejects(Checked action) throws IOException {
        try { action.run(); } catch (IOException expected) { return; }
        throw new AssertionError("Invalid or stalled decode was accepted");
    }
    public static void main(String[] args) throws Exception {
        var generator = java.security.KeyPairGenerator.getInstance("EC");
        generator.initialize(new java.security.spec.ECGenParameterSpec("secp256r1"));
        var identity = generator.generateKeyPair();
        assert DesktopTlsSigning.INSTANCE.canSign(identity.getPrivate(), identity.getPublic());
        assert !DesktopTlsSigning.INSTANCE.canSign(identity.getPrivate(), generator.generateKeyPair().getPublic());
        var rsa = java.security.KeyPairGenerator.getInstance("RSA");
        rsa.initialize(2048);
        assert !DesktopTlsSigning.INSTANCE.canSign(rsa.generateKeyPair().getPrivate(), identity.getPublic());
        long[] now = {10000};
        var state = new DesktopDecodeState(() -> now[0]);
        state.requireOutputProgress();
        state.submitted(); now[0] += 1000; state.requireOutputProgress();
        now[0]++; rejects(state::requireOutputProgress);
        state.decoded(); now[0] += 10000; state.requireOutputProgress();
        assert state.getPending() == 0;
        state.submitted(); state.submitted(); state.decoded();
        assert state.getPending() == 1;
        state.reset(); now[0] += 10000; state.requireOutputProgress();
        state.requireInputProgress(now[0] - 250);
        rejects(() -> state.requireInputProgress(now[0] - 251));
        assert DesktopDecodeState.visibleDimension(1088, 0, 1079) == 1080;
        assert DesktopDecodeState.visibleDimension(1920, 100, 1819) == 1720;
        assert DesktopDecodeState.visibleDimension(1920, null, null) == 1920;
        rejects(() -> DesktopDecodeState.visibleDimension(1920, -1, 1919));
        rejects(() -> DesktopDecodeState.visibleDimension(1920, 0, 1920));
        rejects(() -> DesktopDecodeState.visibleDimension(1920, 40, 39));
        rejects(() -> DesktopDecodeState.visibleDimension(8193, null, null));
        state.presented(1_000_000); state.presented(17_000_000);
        assert state.getMaxPresentationGapNs().getAndSet(0) == 16_000_000;
        state.presented(16_000_000); state.presented(0); state.presented(33_000_000);
        assert state.getMaxPresentationGapNs().getAndSet(0) == 16_000_000;
        state.reset(); state.presented(100_000_000);
        assert state.getMaxPresentationGapNs().get() == 0;
        var texture = new DesktopTextureStatistics();
        texture.consumed(100, 1000); texture.consumed(100, 9000);
        assert texture.getConsumedFrames().get() == 1;
        texture.consumed(200, 17000);
        assert texture.getConsumedFrames().get() == 2;
        assert texture.getMaxGapNs().getAndSet(0) == 16000;
        texture.consumed(-1, 100000);
        assert !texture.getAvailable() && texture.getConsumedFrames().get() == 2;
        texture.consumed(300, 33000); texture.consumed(0, 49000);
        assert texture.getAvailable() && texture.getConsumedFrames().get() == 4;
        assert texture.getMaxGapNs().get() == 16000;
        System.out.println("Raw TLS ECDSA signing, mismatched keys, decode deadlines, crop, presentation gaps and unique texture consumption checks passed.");
    }
}
